# Theorycraft — multi-tenant identity, auth, and Edict's public surface

**Status:** pre-design / theorycraft. Not a PRD, not a spike, not an ADR. Goal: a fresh session (Claude or human) can pick it up cold and decide what Edict's *consumer-facing* multi-tenancy surface should be, and how it ties to a real, cheap, serverless auth story.

**Scope split — read this first.** This doc and its sibling cut the same feature along one clean seam:

- **This doc owns the carry side.** How an authenticated tenant identity *enters* the system, and what Edict's **public surface** looks like (wiring, sender, resolver, the consumer's mental model). It also owns the **auth story**, which the sibling deliberately lists as a non-goal.
- [`theorycraft-tenant-scoped-substrate.md`](theorycraft-tenant-scoped-substrate.md) **owns the honour side.** How each substrate provider folds the tenant scope into its placement keys (grain state, projection rows, claim-check, dead-letter), the per-surface storage strategy, Postgres RLS, and the conformance battery.

The two were written from opposite ends and **meet in the middle at one design decision: tenant rides the message envelope, never ambient context.** They agree on that. Where they differ (the tenant-id *type*), this doc flags it explicitly in Open Question 4 so the eventual ADR reconciles them rather than shipping two answers.

This is pre-v1. Be ambitious. The question is "what is the right long-term shape," never "what is the cheapest patch."

---

## 1. The key realization: two separable problems, one line of contact

Authentication and data-isolation feel like one scary problem. They are not. They couple at exactly one line of code.

- **Auth's only job** is to deposit a *tamper-proof* tenant identifier onto the request principal at the Web/API edge.
- **Edict's only job** is to thread that identifier through every data-crossing boundary so a tenant can never address, subscribe to, or read another tenant's data.

The seam between them: at the edge, read the signed claim and hand it to Edict before the first `SendAsync`. Everything above that line is "pick an identity provider"; everything below it is "Edict mechanics."

The strategic consequence is the most important sentence in this doc: **Edict's design can be auth-provider-agnostic.** The framework should depend on *a tenant id*, not on Entra, not on Keycloak, not on any IdP. The only contract Edict imposes upward is: *the tenant id must arrive as a signed claim the edge has authenticated, never a client-supplied header, subdomain, or command-body field.* That single constraint is the whole interface, and it is what makes "generic regardless of the auth story" a guarantee rather than a hope.

---

## 2. The auth story — you do not run a server, and it is free at this scale

The motivating use case is many Azure App Services for different industries sharing one substrate to cut cost, with a hard regulatory bar: one customer must never see another's data. The operator explicitly does not want to run an auth server, and cost is the primary concern. Both wishes are fully satisfiable in 2026 on the Azure-native stack.

Customers fall into one of two shapes, and the identity model differs:

| Customer shape | Use | The tenant id is | Cost at this scale | Own server? |
|---|---|---|---|---|
| Consumers / self-service signup (B2C/CIAM) | **Microsoft Entra External ID** (the GA successor to Azure AD B2C) | a signed **custom extension claim** you stamp on the user at signup or via a custom claims provider | **Free ≤ 50,000 MAU** | No |
| Enterprise customers who each have their own Entra/Azure AD tenant (B2B/workforce) | **Multi-tenant Entra app registration** (`organizations`/`common` authority) | the built-in **`tid`** claim, a stable GUID signed by *the customer's* Entra | Effectively free | No |

Both are native to the Azure / ASP.NET Core stack (`Microsoft.Identity.Web` wraps the JWT validation), and both deliver the load-bearing property: the tenant claim sits **inside an IdP-signed JWT**, verified against the IdP's published JWKS on every request, so a client cannot forge or alter it. This is the difference between a trustworthy tenant id and a worthless one. A tenant id in a header, a subdomain, or an unsigned cookie is client-supplied and must never gate isolation. A signed claim is tamper-proof.

Microsoft's own multi-tenant validation rule is the entire game restated: *"Always check that the `tid` in a token matches the tenant ID used to store data. Never allow data in one tenant to be accessed from another tenant."* Edict's job is to make that check **structural** rather than something every consumer query has to remember.

**Rejected options, and why:**

- **Azure AD B2C** is a dead end for greenfield: not purchasable by new customers since May 2025, P2 discontinued March 2026. Its successor is External ID. Do not start here.
- **Keycloak / Zitadel (self-hosted) / Ory (self-hosted)** violate the "no own server" constraint outright. They need a container plus a database (Keycloak's H2 is dev-only), which on Azure is an App Service plus a managed Postgres, i.e. real monthly infra and patching burden, for software that is otherwise free. Wrong trade for this operator.
- **Auth0** is excellent and fully managed, but its B2B / Organizations tier jumps to roughly $800/mo past the free tier, which badly violates the cost constraint. **Clerk** ($100/mo for the Organizations add-on) and **Zitadel Cloud** ($100/mo Pro) are cheaper managed B2B options and are reasonable second choices if the operator ever wants off the Microsoft stack, but neither beats free-and-native.

**Recommendation for the auth half: Entra External ID, or multi-tenant Entra, or a hybrid of both.** None cost anything at five customers and a few thousand users, none require running a server, and both produce a signed tenant claim. A hybrid (External ID for self-service consumers, multi-tenant Entra for enterprise SSO customers) covers a mixed book of business and is a documented pattern.

**ASP.NET edge wiring (consumer's code, not Edict's):**

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
// After validation, the tenant claim is on User (ClaimsPrincipal):
//   B2B/workforce:  User.FindFirst("tid")
//   External ID:    User.FindFirst("extension_customerId")  // your custom claim
```

None of the above is an Edict concern. It is here so the design conversation has a concrete, costed, serverless answer to "where does the tenant id come from," and so the reader can see that the *shape* of the claim (GUID `tid` vs string custom claim) is what forces Open Question 4.

---

## 3. Keeping the auth story out of Edict's contracts — the resolver seam

Edict must not reference `Microsoft.Identity.Web`, a `ClaimsPrincipal`, or any IdP. It depends on a tenant id and nothing more. The clean way to express that is a **resolver registered at the edge**:

```csharp
// Web host (consumer code): the ONLY place auth and Edict touch.
services.AddEdictTenancy(httpContext => httpContext.User.FindFirst("tid")?.Value);
```

Edict invokes the resolver to obtain the authenticated tenant id at the moment a command originates, and from there it is pure framework plumbing. Swap the lambda and the entire downstream design is unchanged: that is the "generic regardless of auth story" property made concrete.

Two non-negotiables sit on this seam:

1. **The resolver must read from the authenticated principal, never from the command body.** If a consumer could pass a tenant in a command payload, any caller could craft a message for another tenant's scope (the classic confused-deputy attack). Edict reads the scope from the trusted resolver at the edge and stamps it onto the envelope itself; a tenant value appearing anywhere in arbitrary command data has no place to take effect.
2. **The resolver should not be `HttpContext`-typed in the contract.** Worker hosts, schedule fires, and tests have no `HttpContext`. The framework-facing seam is `Func<EdictTenantId?>` (or an async variant); an ASP.NET *helper package* adapts `ClaimsPrincipal → EdictTenantId`. This keeps `Edict.Contracts` web-framework-free and lets a non-web host supply its own resolver. See Open Question 1.

---

## 4. The public surface — four approaches, with trade-offs

This is the part the substrate doc does not cover and the operator specifically asked about: **what does the consumer actually write?** Four shapes, from most explicit to most invisible. The recommendation is a blend, and the reasoning matters more than the verdict.

### Approach A — explicit parameter on every send

```csharp
await sender.SendAsync(command, EdictTenant.Of(tenantId));
```

The tenant is a first-class argument the consumer passes on every `SendAsync`. An analyzer (`EDICT0xx`) flags any send missing it when tenancy is on.

- **Pro:** maximally explicit, statically enforceable, no magic.
- **Con:** every call site has to fetch the tenant from the principal and pass it, which is tedious and, worse, **does not work for the re-entrant internal paths** (saga `Dispatch`, outbox `SendCommand`, `EdictSchedule` fires) where there is no principal in scope. Those would have to manually re-thread tenant, which is exactly the kind of "easy to forget, fails silently" surface this codebase treats as highest-risk.

### Approach B — edge resolver, framework auto-capture (recommended default)

```csharp
// Wiring once:
siloBuilder.AddEdict().WithTenancy();
services.AddEdictTenancy(ctx => ctx.User.FindFirst("tid")?.Value);

// Consumer code is UNCHANGED from single-tenant Edict:
await sender.SendAsync(new PlaceOrder(orderId, ...));
public sealed class OrderCommandHandler : EdictCommandHandler<OrderState> { ... }
```

The framework calls the resolver at the `SendAsync` boundary, folds the tenant onto the envelope, and the consumer's handlers and sagas never name tenant at all. This fits Edict's defining philosophy: the framework hides Orleans-isms; tenancy becomes another invisible, framework-managed dimension like the trace context already is.

- **Pro:** the consumer mental model stays "write `OrderCommandHandler`," and the tenant dimension is impossible to forget because the consumer never touches it. It composes with sagas and schedules (see the propagation loop below).
- **Con:** "ambient at the edge" sounds like the ambient-context trap the sibling doc rightly rejects. It is not, *if* ambient is used only as a capture-once relay and the durable carrier remains the envelope. That distinction is the crux of the whole design and is spelled out next.

### The propagation loop (why B is correct and A is not)

The edge resolver only fires for the *first* command in a chain. Sagas dispatch commands, the outbox sends commands, and `EdictSchedule` re-fires, all from **inside a grain with no `HttpContext`**. So the design cannot end at the edge. The full loop:

1. **Edge:** resolver yields the tenant → framework stamps it onto the command envelope (durable data).
2. **Grain entry (every grain, every turn):** the framework reads the tenant *off the envelope* and seeds it into `RequestContext` for the duration of that turn, purely as an **intra-turn relay**. It also feeds the enforcement filter (Section 5).
3. **Internal send within the turn** (`Dispatch`, schedule fire, outbox `SendCommand`): the framework captures the tenant from that intra-turn `RequestContext` → stamps it onto the *new* envelope.

`RequestContext` is used here **only** as a same-turn convenience so a consumer's saga code does not have to manually thread tenant into `Dispatch`. It is re-seeded from the envelope at every grain entry, so it never has to survive a stream hop, a reminder, or a timer, which is precisely the thing the sibling doc proves it cannot do. **The envelope is the carrier; `RequestContext` is a per-turn relay re-derived from the carrier.** The two docs do not conflict: the sibling rejects ambient as the *carrier*; this doc uses ambient only as the *relay*, seeded from the carrier. This reconciliation is the single most load-bearing idea here.

This is also the answer to the sibling's failure-mode 6 ("tenant change mid-flow"): the tenant is captured on the envelope at raise time and re-stamped from the upstream envelope at every hop, so a flow that began as tenant A stays tenant A through every async hop, retry, and schedule fire.

### Approach C — tenant-bound sender resolved from DI scope

```csharp
// Framework resolves an IEdictSender already bound to the current tenant.
public OrdersController(IEdictSender sender) { ... } // sender is tenant-scoped per request
```

A request-scoped `IEdictSender` is constructed already carrying the tenant, so a send *cannot* omit it.

- **Pro:** strong compile-time-ish guarantee; no per-call argument.
- **Con:** DI-scope-bound senders interact awkwardly with Orleans grain activation (grains are not request-scoped), and it does not solve the re-entrant internal paths any better than B does. Mostly a more rigid spelling of B with more types. Keep B.

### Approach D — middleware establishes an `EdictTenantScope`

An ASP.NET middleware reads the claim and sets an `EdictTenantScope` the framework consumes. This is just B with the resolver inverted (push vs pull). The pull-resolver of B is simpler to register and easier to supply from a non-web host. Prefer B.

### Verdict

**Ship B as the consumer-facing default, keep A as the documented escape hatch.** B gives the invisible, hard-to-misuse common path that fits the brand; A's explicit `SendAsync(command, EdictTenant.Of(...))` overload is the right tool for genuinely context-free origins (a one-off admin script, a data-import worker, a test) where there is no principal to resolve from. C and D add types without adding safety over B.

---

## 5. Enforcement — the call filter as the structural "check tid matches"

Carrying the tenant is necessary but not sufficient; something must *reject* a mismatch. Orleans gives exactly the seam: an `IIncomingGrainCallFilter` that runs before every grain call (including stream delivery, which is a grain call through an extension) and can throw to refuse the call before it reaches the grain.

```
On every grain entry, when tenancy is on:
  envelopeTenant := tenant read off the inbound envelope / request context
  keyTenant      := tenant parsed from the target grain key
  if envelopeTenant != keyTenant  ->  throw EdictCrossTenantAccessException   // never reaches the grain
```

This is the structural form of Microsoft's "always check `tid` matches the tenant used to store data." It is framework-internal (no consumer surface beyond `WithTenancy()` turning it on), and it is the layer that catches a *coding* bug, not just a malicious caller. Prior art exists for the raw-Orleans version of this: the **`Orleans.Multitenant`** library (Applicita) assembles tenant-in-key, call-filter enforcement, stream isolation, and storage partitioning, and throws `UnauthorizedException` on cross-tenant access. Worth reading before designing. Its gap, and therefore Edict's distinct contribution, is that it knows nothing about the CQRS seams Edict adds on top: the outbox, the dead-letter projection, claim-check spill, and table projections. Those are where the real Edict-specific work lives (the sibling doc maps them).

---

## 6. The grain-key question, and a nuance that sharpens where the work is

Should the tenant be folded into the grain key (`{tenant}/{routeKey}`) or left out, with isolation enforced only at storage?

A sharpening observation first: `[EdictRouteKey]` values are random `Guid`s the consumer mints per aggregate. Tenant A's order and tenant B's order already have *different* Guids, so grain activations, grain-state rows, and Guid-keyed streams do not physically collide today. The cross-tenant-collision disaster mostly does not happen on the per-aggregate path. This means **the value of tenant-in-the-key is not collision avoidance** (random Guids handle that). Its value is:

1. **Enforceable authorization** — a caller who obtains another tenant's Guid (guessed, leaked, or via a bug) is still rejected by the call filter in Section 5, and
2. **The fleet-wide and non-Guid surfaces that random Guids do not protect at all** — the dead-letter singleton partition, global/singleton table projections, and claim-check enumeration (all enumerated in the sibling doc, with dead-letter as the sharpest: it deliberately pools every tenant's failure, payload bytes included, into one `"deadletter"` partition).

**Implication for the public surface:** the consumer's `[EdictRouteKey]` does **not** change. It stays the consumer's domain `Guid`. The tenant component is folded into the *physical* grain/stream/storage key by the framework and is invisible in consumer code. The composite is a framework-internal concern, which keeps the brand promise intact.

This reframes the central design fork honestly:

- **Structural (tenant-in-key + call-filter):** isolation rides identity, survives every hop because identity does, and a coding bug is *rejected* rather than silently leaking. Invasive: touches routing, generators, both persistence providers' key derivation, the publish/subscribe path. The sibling doc's honour-side work is mostly this.
- **Storage-only (keys unchanged, RLS / partition backstop):** less invasive, leans on the Postgres RLS wall and per-surface prefixes. But on its own it cannot enforce authorization at the grain boundary, and it does nothing for the fleet-wide surfaces unless each is independently tenant-tagged.

**Lean: structural, as the primary, with the storage backstop as defense-in-depth underneath** (exactly the layered model both docs converge on). The reason is the regulatory bar: a single missed `WHERE tenant_id` should be *caught*, not catastrophic, and only an engine-level backstop plus an identity-level enforcement together deliver "no single bug leaks."

---

## 7. Do not tax the single-tenant common case

The Sample app and most consumers are single-tenant and must pay nothing for a feature they do not use. Make tenancy **opt-in at wiring time** (`AddEdict().WithTenancy()`). Off by default: no envelope field cost (or a fixed default-tenant sentinel that compresses away), no call filter, no analyzer requirement. On: the analyzer requires a resolved tenant on every origin send, the call filter enforces, and the providers fail closed when a scope is missing. This mirrors the codebase's standing "common-case no-overhead" principle: escape hatches must not tax the path that does not use them.

Fail-closed on "tenancy on but tenant missing" is non-negotiable: a typed `Edict*` throw at the boundary (ADR-0041 philosophy), never a silent fall-through to a default partition, because the silent fall-through *is* the breach.

---

## 8. A cohesive picture of the proposed public surface

Pulling Sections 3–7 together, here is the whole consumer-facing footprint of the recommended design. Everything not shown is unchanged from single-tenant Edict.

```csharp
// 1. Silo + web wiring (the only new surface):
siloBuilder.AddEdict().WithTenancy();                                   // turns on key-folding, filter, fail-closed
services.AddEdictTenancy(ctx => ctx.User.FindFirst("tid")?.Value);      // edge resolver (ASP.NET helper)

// 2. Consumer handlers, sagas, projections — IDENTICAL to single-tenant:
public sealed class OrderCommandHandler : EdictCommandHandler<OrderState> { /* no tenant anywhere */ }

// 3. Originating a command at the edge — tenant captured automatically (Approach B):
await sender.SendAsync(new PlaceOrder(orderId, ...));

// 4. Escape hatch for context-free origins (Approach A):
await sender.SendAsync(new ImportOrder(...), EdictTenant.Of(tenantId)); // worker / admin / test

// 5. Reading a tenant-scoped read model — resolves within the ambient tenant, cannot cross:
OrderRow row = await repository.GetAsync<OrdersByStatusProjection, OrderRow>(orderId);
```

New consumer-visible names, all `Edict`-prefixed per the brand rule: `WithTenancy()`, `AddEdictTenancy(...)`, `EdictTenant` / `EdictTenantId`, and the `SendAsync(command, EdictTenant)` overload. That is the entire public surface. The call filter, the envelope field, the composite key, the grain-entry relay, and the provider key-folding are all framework-internal.

---

## 9. Open design questions (public-surface-specific)

The sibling doc's open questions cover the honour side (per-surface strategy, RLS, dead-letter pool, stream scoping). These are the ones this doc adds.

1. **Resolver signature and host-neutrality.** Recommend the framework seam be `Func<EdictTenantId?>` (sync) with an async variant if a resolver needs I/O, and ship a small ASP.NET adapter (`AddEdictTenancy(Func<HttpContext, string?>)` or a `ClaimsPrincipal` overload) in a web-facing helper package so `Edict.Contracts` stays free of `Microsoft.AspNetCore.*`. Decide whether the helper lives in a new tiny package or in an existing consumer-facing one.

2. **Resolver timing and failure.** When exactly is the resolver invoked, and what happens if it returns null while tenancy is on? Recommend: invoked at the originating `SendAsync`, null is a fail-closed typed throw at the edge (not at the grain), so the breach-prevention failure surfaces synchronously to the caller, before anything is persisted.

3. **Where the relay lives.** The intra-turn relay (Section 4) needs a well-defined home: `RequestContext` re-seeded at grain entry by the framework. Confirm this composes with the existing trace-context capture (`ActivityExtensions.CaptureToRequestContext`), which already proves the pattern. Likely they sit side by side under the same capture point.

4. **Tenant-id *type* — a real tension with the sibling doc, must be reconciled in the ADR.** The sibling recommends a `Guid`-backed `EdictTenantId` (matches the route-key precedent, index-friendly). The **auth research pushes the other way**: a B2B `tid` is a GUID, but an External ID custom claim, a Keycloak realm, or any non-Microsoft IdP commonly yields an arbitrary *string* slug. For "generic regardless of auth story," a `Guid` constraint quietly excludes a chunk of viable IdPs. Recommend: an **opaque, length-bounded, charset-validated `string`** at the contract (the lowest common denominator across IdPs), with a `Guid` fast-path internally where every substrate likes it. The charset validation is load-bearing because the value becomes part of partition keys and blob paths (injection / separator-collision risk, see the sibling's Postgres `/`-in-key and Azure forbidden-character notes). **This is the one place the two docs currently disagree; the ADR picks one.**

5. **`SendAsync` overload ergonomics.** With Approach B as default, the `SendAsync(command, EdictTenant)` overload (Approach A) is the explicit override. Decide precedence: if both an ambient tenant and an explicit argument are present, the explicit argument should win (it is the deliberate override), and a *mismatch* between an explicit argument and an established ambient tenant should arguably throw rather than silently prefer one. Lock this, because "which tenant wins" is a security decision, not an ergonomics one.

6. **Analyzer scope.** The `EDICT0xx` analyzer requires a tenant on origin sends when tenancy is on. But internal re-entrant sends (saga `Dispatch`, schedule fire) get their tenant from the relay, not from consumer code, so the analyzer must *not* flag those. Defining "origin send vs framework-relayed send" precisely is the subtle part. Treat the analyzer as a top test target (silent-failure risk).

7. **Telemetry cardinality.** A `tenant` span/metric dimension is useful but unbounded tenant labels blow up metric cardinality. Recommend tenant on spans (sampled) freely, tenant on metrics only via a bounded allow-list or exemplar. (Echoes the sibling's OQ9.)

---

## 10. Relationship to the sibling doc, and non-goals

**This doc + the sibling are one feature.** A future ADR (or two) should cite both. The clean division to preserve:

- **Carry side (here):** auth story, the resolver seam, the public surface (wiring, sender, `EdictTenant`), the propagation loop, the call-filter enforcement, fail-closed, single-tenant zero-cost.
- **Honour side (sibling):** per-surface storage scoping, Postgres RLS, dead-letter pool decision, stream-namespace scoping, the cross-tenant conformance battery (the real deliverable).

They share three locked decisions: **envelope-not-ambient** (the carrier), **fail-closed** when tenancy is on and tenant is missing, and **opt-in at wiring** so single-tenant pays nothing. They have **one open disagreement** to settle: the tenant-id type (Open Question 4 here vs the sibling's OQ2).

**Non-goals (in addition to the sibling's):**

- **Edict will not authenticate.** The edge authenticates with the IdP; Edict carries and enforces an already-trusted claim. Stated here and in the sibling on purpose.
- **Edict will not depend on an IdP SDK.** No `Microsoft.Identity.Web`, no `ClaimsPrincipal` in `Edict.Contracts`. The resolver `Func` is the only contact point; IdP adapters are consumer-side or a thin optional helper.
- **No tenant-migration / re-keying tooling in v1** (matches sibling).
- **Per-tenant *deployment* automation is the other isolation model** (physical isolation, one substrate per tenant), already possible today with zero framework change, and remains the recommendation for the highest-risk regulated tenants. This doc is the pooled-compute model.

---

## Suggested first slice (carry-side)

The sibling proposes a honour-side first slice (Postgres table-projection scoping + one cross-tenant conformance test). The complementary carry-side slice that unblocks it:

1. `EdictTenant` / `EdictTenantId` in `Edict.Contracts`, plus the tenant field on the command/event envelope (`GrainEnvelope<TPayload>`).
2. `AddEdictTenancy(resolver)` + `WithTenancy()` wiring; the resolver fires at the originating `SendAsync` and stamps the envelope (Approach B).
3. The grain-entry relay: read tenant off the envelope into `RequestContext` for the turn, and re-stamp it onto sends raised within the turn (proves the saga/schedule path).
4. Fail-closed: tenancy on + null resolver result throws a typed `Edict*` at the edge.

That slice delivers a tenant that demonstrably survives an edge send, a saga `Dispatch`, and a schedule fire, all on the envelope, which is the exact carrier the honour-side slice then reads when it folds the tenant into a partition key. Built together, the two first slices produce one end-to-end vertical: authenticated edge send to tenant-scoped row, with a tenant-B read of the identical key returning empty.

---

## Sources

Auth landscape and Orleans isolation patterns were researched against current (2026) primary docs. Key references:

- Microsoft Entra External ID overview & pricing — https://learn.microsoft.com/en-us/entra/external-id/customers/overview-customers-ciam , https://learn.microsoft.com/en-us/entra/external-id/external-identities-pricing
- Add attributes / custom claims to the token (External ID) — https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-add-attributes-to-token
- Multi-tenant app & claims validation ("check `tid` matches") — https://learn.microsoft.com/en-us/entra/identity-platform/claims-validation , https://learn.microsoft.com/en-us/entra/identity-platform/howto-convert-app-to-be-multi-tenant
- Azure AD B2C status / deprecation — https://learn.microsoft.com/en-us/azure/active-directory-b2c/faq
- Orleans grain identity, request context, call filters, streaming, persistence — https://learn.microsoft.com/en-us/dotnet/orleans/grains/grain-identity , https://learn.microsoft.com/en-us/dotnet/orleans/grains/request-context , https://learn.microsoft.com/en-us/dotnet/orleans/grains/interceptors , https://learn.microsoft.com/en-us/dotnet/orleans/streaming/streams-programming-apis , https://learn.microsoft.com/en-us/dotnet/orleans/grains/grain-persistence/
- `Orleans.Multitenant` (Applicita) — prior art for layers 1–3 — https://github.com/Applicita/Orleans.Multitenant
- SaaS isolation framing (silo/pool/bridge) and Postgres RLS depth live in the sibling doc's Sources.
