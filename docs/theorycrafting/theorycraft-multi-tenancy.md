# Theorycraft — multi-tenancy

**Status:** design-grilled and settled (2026-06-16). Not yet a PRD. The problem framing and the design decisions below were walked through a `grill-with-docs` session; §5 onward records *decisions* (R1–R13), not open options. The remaining step is to cut the PRD and slices from §12.

**This doc supersedes and replaces** two earlier drafts (`theorycraft-multi-tenant-identity-and-surface.md` and `theorycraft-tenant-scoped-substrate.md`), which were working attempts that have been deleted. It folds in three things the earlier drafts predated or got wrong: that the carry machinery already shipped for `EdictPrincipal`, that "tenant" is one of three identity axes rather than the identity, and the verified Orleans facts about how a tenant can actually ride the grain key. Where this doc states an Orleans mechanic as fact, it was confirmed against Orleans source (`DefaultStreamIdMapper`) and the `Orleans.Multitenant` library, not assumed.

This is pre-v1, with no released consumers and no compatibility constraints. Be ambitious. The question is "what is the right long-term shape," never "what is the cheapest patch." Priorities, in order: **ergonomics first, correctness second, performance third.** One exception to that order: where a decision is the difference between *verifiable* isolation and *hoped-for* isolation, correctness leads, because a silent cross-tenant leak is a compliance breach with fines attached, not a bug.

---

## 1. The problem

A consumer runs one Edict deployment (one Orleans cluster, one substrate) serving a mixed book of business:

- **B2C consumers**: individuals who self-serve, in the 10,000s.
- **B2B businesses**: companies that consume the product, often via API.
- **B2B2C employees**: the thousands of people inside those businesses.

The hard constraint is regulatory: one customer must never read, edit, or even observe another customer's data. A leak is not a bug, it is a compliance breach with fines attached.

The concrete everyday feature, and the one to design around, is positive rather than defensive: **a business lists and edits details about its own employees.** Isolation is what stops that same list or edit from ever reaching another business's employees.

**One deployment mixes tenant and non-tenant work.** The same app has a public-facing side (a marketing site, anonymous orders, a public catalogue everyone sees) *and* a B2B side (companies managing their employees). Tenancy is therefore **not** a silo-wide mode; it is a per-aggregate property, and the design must let both flavours coexist in one cluster. This requirement is load-bearing: it is what makes §5's "per-aggregate, not global" decision unavoidable.

---

## 2. Tenant is one identity axis of three

The word "tenant" only makes sense once three distinct identity questions are separated. Edict already names two of them in `CONTEXT.md`:

| Axis | Question | Example | Status in Edict |
|---|---|---|---|
| **Principal** | Who acted? | the employee who clicked | **Shipped** (`EdictPrincipal`, ADR-0063/0064) |
| **Tenant** | Whose data is this? Which isolation wall? | the employer the employee belongs to | **This design** |
| **Data subject** | Who is the data about? (GDPR) | the customer whose record was edited | **Deferred** (CONTEXT.md) |

The load-bearing correction over the earlier drafts: **tenant and principal are orthogonal.** A B2B2C employee authenticates as themselves (principal is their `oid`/`sub`), but the data they touch belongs to their employer (tenant is the business). One person, two identity values, two jobs.

This fixes "what is a tenant": **a tenant is an opaque isolation key, and Edict does not care whether it stands for one human or a 5,000-employee company.** A self-service B2C consumer can be a tenant-of-one; a B2B business is a single tenant with thousands of principals inside it. Both are opaque keys folded into placement, exactly as `EdictPrincipal` is already an opaque consumer string Edict refuses to interpret.

---

## 3. Two boundaries, and the bearer-capability problem

There are two different walls, and conflating them is the central trap:

- **Boundary A, the company wall**: Company A versus Company B. Cross-tenant.
- **Boundary B, within-tenant authorization**: inside Company A, may *this* user edit *this* employee's salary, or only their own record?

### What exists today: zero isolation, and the route key is a bearer capability

Traced through the code, an inbound id is passed verbatim to `GetGrain` with nothing mixed in:

- Command: `EdictSender` does `key = route.RouteKeySelector(command)` then `GetGrain<IEdictCommandHandler>(key, ...)` (`Edict.Core/Commands/EdictSender.cs:43-45`). The key is the `[EdictRouteKey]` Guid read straight off the caller's command.
- Single read: `IEdictProjectionReader.ReadAsync(Guid key)` routes that caller-supplied Guid verbatim.
- List read: `IEdictListProjectionReader.QueryPartitionAsync(string partitionKey)` does `Guid.Parse(partitionKey)` then `GetGrain` verbatim.
- `EdictPrincipal` is stamped for audit and **never consulted in routing**.

So today, a Company A user who obtains Company B's employee Guid sends `UpdateEmployee(companyB_id)` and is routed straight into Company B's grain, and edits it. The Guid *is* the access: whoever holds it gets in. This is the status quo, and it means isolation today is entirely the consumer's, on every path, with no framework backstop.

### Whose problem each boundary is

The honest split (do not soften this):

| Concern | Owner | Edict's role |
|---|---|---|
| Authenticate the user | Consumer + IdP | None |
| `user → tenant` map | Consumer | None: it is the consumer's data |
| Tenant sourced from auth, never the request body | Consumer (resolver wiring) | Shape the seam to discourage; cannot enforce |
| **Cross-company isolation, given a correct tenant** | **Edict** | **Structural: the headline value** |
| Catch a forgotten isolation filter | Edict (defense-in-depth) | Yes: structural beats per-query checks |
| Within-company authz (which user, which employee, which field) | Consumer | None: the wall is the company, not the user |
| Data-subject / GDPR erasure | Nobody yet | Deferred |

The crisp statement: **Edict can guarantee a Company A session only ever addresses Company A's data. It cannot guarantee the right person within Company A is doing the right thing, and it cannot save a consumer whose tenant map hands out the wrong tenant.** The first is worth building. The other two are irreducibly the consumer's. Edict's guarantee is conditional on a correctly resolved, authenticated tenant: garbage in (a buggy `employee → company` lookup, a body-sourced tenant) means garbage out, and Edict trusts the resolved tenant absolutely.

The single strongest thing Edict offers: **it demotes the route-key Guid from a bearer capability to a within-tenant identifier**, so a stolen *cross-company* id becomes useless. It does nothing for a stolen *same-company* colleague's id (boundary B), and should not pretend to.

---

## 4. The carry side: reuse the principal machine, do not reinvent it

How the tenant *enters and travels* is a solved problem in this repo. The audit feature (ADR-0063/0064) already ships the exact propagation machine an earlier draft tried to design from scratch. The tenant carry is "a second value on rails that already exist."

| What the tenant carry needs | Already shipped as | Tenant sibling |
|---|---|---|
| Edge resolver `Func`, no IdP / `ClaimsPrincipal` reference | `IEdictPrincipalResolver`, registered by `AddEdictAudit(Func<…, EdictPrincipal?>)` | `AddEdictTenant(Func<IServiceProvider, EdictTenantId?>)` |
| A durable identity field on the wire | `EdictPrincipal?` on `EdictCommand`/`EdictEvent`, beside the Correlation Id | `EdictTenantId? Tenant`, sibling top-level field |
| Per-turn relay re-seeded at grain entry | `PrincipalRelay.Seed`/`Current` (`Edict.Telemetry`) | `TenantRelay` (byte-for-byte sibling) |
| Origin send vs framework-relayed send | `EdictPrincipalStamper` gating on `IsCrossTurnLink()` | a sibling tenant stamper |
| Fail-closed when on-but-missing | `EdictMissingPrincipalException` | `EdictMissingTenantException` |
| The opt-in analyzer (origin sends only) | `OriginSendPrincipalAnalyzer` (EDICT023) | a new selective analyzer (§5, R3) |
| The explicit escape-hatch overload | `SendAsync(command, EdictPrincipal)` | `SendAsync(command, EdictTenantId)` |

### The tenant-id type is settled

`EdictPrincipal` already decided the shape for an identity value on the same envelope: an opaque consumer string, minted via `Of(...)`, "because format is the identity provider's concern." A `Guid`-backed `EdictTenantId` beside a string `EdictPrincipal` would be incoherent, and a `Guid` constraint silently excludes viable IdPs (an Entra External ID custom claim or a non-Microsoft realm slug is not a GUID). **`EdictTenantId` is an opaque string** — with one critical difference from the principal: because the tenant goes *into the key and the storage path* (§5, §6), its charset validation is a **security control**, not hygiene. See §10, where this is the centre of gravity.

### The principal-to-tenant mapping is the consumer's, and is usually a lookup

Reading the tenant straight off the token (`User.FindFirst("tid")`) only fits pure B2B workforce SSO. It is wrong for the motivating cases:

- **B2B2C employee**: the token identifies the employee (principal); the tenant is their employer, an `employee → business` lookup the consumer owns.
- **B2B partner over API**: authenticated by API key, the tenant is a row in the consumer's database, not an Entra `tid`.
- **B2C consumer**: a tenant-of-one, or not a participant in tenancy.

So the resolver seam is `Func<…, EdictTenantId?>`, and what the consumer puts behind it is *their* mapping. Edict's only upward contract: the value must derive from something the edge authenticated, never from the command body (confused-deputy).

### Generalized carrier versus second carry: split (decided)

The principal carry and the tenant carry are near-identical plumbing, but the parts that differ (opt-in switch, fail-closed type, validation, and the entire downstream: principal feeds audit capture, tenant feeds placement plus the call filter) differ *per axis no matter what*.

**Decision: unify only the one seam where drift is a security bug, duplicate the rest.**

1. **Unify the seed-at-entry.** The ~4 grain-entry sites that call `PrincipalRelay.Seed(...)` (`EdictCommandHandler.cs:484`/`:258`, `EdictSaga.cs:361`/`:626`) call a single `OriginIdentity.Seed(principal, tenant)`. A future grain-entry path that seeds the principal but forgets the tenant is a *silent cross-tenant leak* — the worst failure class here; one call makes it unrepresentable.
2. **Keep the rest as honest siblings (second carry)**: separate resolvers, stampers, fail-closed types, validation. Two focused stampers beat one branchy one.
3. **Keep `Principal` and `Tenant` as sibling top-level fields on the base**, following the shipped precedent (principal landed *on* `EdictCommand`/`EdictEvent`, and `EdictEvent` already inherits it), so the shipped wire shape does not churn.
4. **Extract the remaining commonality under rule-of-three**, when the data-subject axis lands and all three are concrete.

---

## 5. Keying: how the tenant changes which grain (the heart)

This is where the principal analogy ends. **The principal carries but never routes** — it is stamped onto the envelope and is audit-only; nothing in `EdictSender` consults it after stamping. The tenant's whole reason to exist is to change *which grain a command, event, or read addresses*. The carry gets the tenant *to* the grain; the keying decides *which* grain, and only this half is new mechanism.

### R1 — uniform string keys, always

The consumer never names a grain key type. The three framework dispatch interfaces — `IEdictCommandHandler`, `IEdictEventConsumer`, `IEdictScheduleFireable` — are framework-internal (`IEdictCommandHandler`'s own doc says *"no human authors or reads this interface"*); the consumer writes `[EdictRouteKey]` on a property and subclasses a base. So a Guid/string key-type *mix* (some interfaces `IGrainWithGuidKey`, some `IGrainWithStringKey`) buys the consumer nothing and costs the generator a second codegen path.

**Decision: all three dispatch interfaces become `IGrainWithStringKey`, always, for every consumer including single-tenant.** Per-aggregate tenancy is then a difference of key *value and behaviour*, never key *type*:

- **Public aggregate**: `Compose` returns `routeKey.ToString()` (`"d3b07384-…"`). No tenant required, fail-closed never fires, no call filter.
- **Tenant-scoped aggregate**: `Compose` returns `$"{tenant}|{routeKey}"`. Tenant required (fail-closed), call filter enforces (§7).

This is the one-way door — Orleans fixes a grain's key type at compile time, so this cannot be Guid-off / string-on — and it is *accepted*, because its cost is invisible: storage is already TEXT (`grainId.Key.ToString()` to Postgres / the Azure blob path), there is no grain-placement penalty for string keys, and key-type performance is priority three and negligible. Pre-v1 with no consumers, this is the moment the door is free. The earlier framing of this as an agonizing "tax on single-tenant" was wrong: single-tenant declares no tenancy, every aggregate is "public" mode, the key is just the stringified Guid, and the only observable difference is the key's textual shape.

### R2 — `[EdictTenantScoped]` on the route-key *type* (coherence by construction)

A single logical aggregate (Order) is a *cluster* of message types — every command and event carrying `OrderId` — plus its handler and subscribing projections, and they must **all** agree on tenancy or the leak is exactly the failure we guard against (command lands on `acme|orderId`, event streams to bare `orderId`). That coherence requirement, not aesthetics, decides the marker.

**Decision: the tenancy marker is an attribute on the route-key *type*, and `[EdictRouteKey]` widens from "single `Guid` property" to accept a typed key.**

```csharp
[EdictTenantScoped]
public readonly record struct EmployeeId(Guid Value);

// every message of the aggregate, by construction:
public sealed partial record AddEmployeeCommand(
    [property: EdictRouteKey] EmployeeId EmployeeId, …) : EdictCommand;
```

Tenancy is declared **once**, on the type. Every message whose route key is `EmployeeId` is tenant-scoped — drift across the cluster is *impossible*, not merely validated, and a boot-time coherence validator collapses to "does the route-key type carry the attribute." Typed ids (cannot pass an `OrderId` where an `EmployeeId` is wanted) come free. The cost is real and accepted: widening `[EdictRouteKey]` touches the route-key selector, MessagePack serialization, storage stringification, and the generator — the single largest change in the effort (§9, breaking). The rejected alternative, `[EdictTenantScoped]` on each message class, is cheaper to author but reintroduces drift (mark four of five messages, the fifth silently leaks) and needs a fragile join to validate. Coherence-by-construction is the long-term shape.

### R3 — the analyzer (compile-time half of fail-closed)

A new analyzer, sibling to EDICT023, flags a bare origin `SendAsync(command)` **when the command's route-key type carries `[EdictTenantScoped]`**. It is *stronger* than EDICT023: principal-attribution is a DI fact an analyzer cannot see (so EDICT023 is opt-in), but `[EdictTenantScoped]` is **static source**, so the analyzer is **selective** (never fires for non-tenant aggregates, so never noise) and can default **on**. Sending an aggregate the consumer explicitly marked tenant-scoped without a tenant is almost always a real bug; the one false positive (a tenant wired via an ambient resolver, runtime-stamped) gets EDICT023's same per-site suppression. It fires on **command origin sends only** — events are raised inside a handler and the framework stamps their tenant from the relay, so there is no consumer-authored site to flag.

### The single `Compose` chokepoint

The route-key value plays a triple role through three *separate* generator-emitted sites — the command-handler grain key (`EdictSender.cs:43-45`), the stream key (`StreamId.Create(streamName, routeKey)`, `PublishEventExecutor.cs:27-28`), and the projection/saga grain key (implicit subscription activates the consumer grain with the stream key; partition is `GetPrimaryKey().ToString()`, `EdictListProjectionBuilder.cs:59`). **All three, and every storage-placement site, route through one internal `Compose(tenant, routeKey)`.** One place to fold means no place to forget; a generator-coverage architecture guard (§10, R11) asserts every tenant-scoped emit site goes through it, so a future refactor cannot silently drop one.

### Verified Orleans facts (not assumptions)

An internal exploration first concluded tenant-in-key was impossible because implicit subscriptions require a bare-Guid grain key. **That is wrong**, confirmed against Orleans source and `Orleans.Multitenant` (v4, Orleans 10 / .NET 10, Edict's exact target):

- StreamId-to-grain-key resolution goes through `IStreamIdMapper`. The default mapper branches on the grain's key type: `IGrainWithStringKey` receives the stream key **verbatim** (no custom mapper, no Guid requirement); `IGrainWithGuidKey` throws unless the stream key is a bare Guid (the constraint the exploration mistook for a universal law); `IGrainWithGuidCompoundKey` folds the stream *namespace* into the key extension.
- `Orleans.Multitenant`'s proven pattern is exactly the **string-key prefix** `<tenant>|<keyWithinTenant>` baked into `GrainId.Key` and `StreamId.Key`. This is the prior art R1/R2 follow.

This is why **Slice 0 is a mechanical spike** (§12): prove string-key + implicit-subscription routing end-to-end in Edict's own wiring before committing the keying layer. Source and prior art say it works; see it work here once.

---

## 6. Crossing the boundary: explicit-only, fail-closed on accident

In the mixed app (§1), public and tenant aggregates *will* interact. Three concrete crossings, and the single rule that governs them.

**The rule: the relay never changes tenancy mode or tenant value implicitly; only an explicit, auditable send can cross the wall.** Same-mode hops auto-propagate via the relay (tenant→same-tenant, public→public) — the common path, zero ceremony. Crossing modes requires a deliberate, visible act, and the framework fails closed on an accidental cross.

1. **Public → tenant (establishing).** A public "register your company" flow has no ambient tenant yet must create `acme`'s first data. The framework will *not* invent a tenant from a public context; the consumer names it once and visibly: `SendAsync(command, EdictTenantId.Of(payload.CompanyId))`. This is the one public→tenant bridge, auditable. It is exactly what the Sample's onboarding flow demonstrates (§11).
2. **Tenant → public (de-scoping).** A tenant-scoped handler raising an event a *fleet-wide* operator projection consumes (a cross-tenant admin dashboard). Allowed, but the global projection is a *deliberately* un-`[EdictTenantScoped]` aggregate reachable only on the privileged operator path — never a normal tenant session.
3. **Tenant A → tenant B.** Forbidden by default: the relay tenant is sticky, and re-keying mid-chain requires the explicit overload, at which point the call filter throws unless the caller is on the privileged path. Accidental A→B cannot happen silently.

---

## 7. The headline read, and runtime enforcement

### The everyday feature is a within-tenant list read (R6)

"List my employees" is an `EdictListProjectionBuilder` (List species, ADR-0061) keyed so the **partition is the tenant**, read via `IEdictListProjectionReader.QueryPartitionAsync`. **The read path scopes by the ambient tenant exactly as the write path composes it — the consumer never supplies the tenant on a read.** The same edge resolver seeds the relay at the read edge; the consumer queries by logical within-tenant scope ("everything in my partition", "this employee id"), and the framework composes `ambientTenant` into the partition. A consumer literally *cannot express* "Globex's partition" because they never pass a tenant. Within-tenant authz (which employee *this* user may see) stays the consumer's job. The cross-tenant denial test is the headline's shadow: Globex's session running the identical query returns empty, by construction, not by a permission check. The read API needs a shape change so a raw partition key cannot bypass the framework's tenant composition.

### The call filter is verifiable defense-in-depth (R4)

Carrying and folding the tenant makes cross-tenant addressing structurally impossible on the common path. The call filter is the backstop that catches a *coding* bug — some path that formed a key without folding, or a drift between the three `Compose` sites. An `IIncomingGrainCallFilter` on tenant-keyed grains parses the tenant out of the **grain's own key** and compares it to the relay's ambient tenant, throwing `EdictCrossTenantAccessException` on mismatch. On the happy path key-tenant == relay-tenant always (Compose used the relay tenant), so the filter is a comparison that always passes — silent. It only fires when they *diverge*, which is precisely an accidental cross or a bug. This check is only possible because R1/R2 make the key *parseable*: it is the concrete payoff of a verifiable key over an opaque hash.

**The privileged hole.** The two legitimate crossings (the operator reading a fleet-wide projection, the explicit establishing send) pass via a single explicit opt-out: the privileged path sets a `RequestContext` cross-tenant-authorized flag (set only by the explicit overload and the operator console wiring), and the filter honors it *and records it* — the cross is a span event and an audit row, never silent. Everything without that flag is held to strict key == relay equality.

---

## 8. The honour side: storage placement per surface (R5)

Once the tenant rides the key (§5), per-aggregate placement follows for free, because every per-aggregate surface derives placement from the grain key. The exceptions are the **fleet-wide and singleton** surfaces, where the key is a fixed constant, so they pool all tenants today and must be scoped explicitly.

| Surface | Placement today | Tenant fold |
|---|---|---|
| Grain state (Postgres / Azure) | `grain_id` TEXT / blob path = grain key | follows the key automatically |
| Per-aggregate projection (State, List) | partition = `GetPrimaryKey().ToString()` | follows the key automatically |
| **Claim-check (Azure blob / Postgres)** | blob path / UUID keyed by bare `EventId` | **mandatory fold** — the tenant goes into the path (`{tenant}/{eventId}`); this surface holds the *largest* payloads, so leaving it shared leaks exactly the big ones |
| **Dead-letter** | literal `"deadletter"` partition, payload bytes, fleet-wide | **operator-scoped + tenant-tagged**; rows carry the tenant so the operator filters and a future per-tenant view is possible; no tenant-facing read in v1 (deferred) |
| **Singleton / global List projection** | fixed `DefaultPartitionKey` constant | the deliberate un-`[EdictTenantScoped]` operator opt-out (§6.2), visible by the absence of the marker |

**The substrate asymmetry, stated openly.** After keying, the two backends are *not* equally defensible:

- **Postgres** gets a DB-enforced second wall: Row-Level Security, `SET LOCAL edict.tenant = '…'` per transaction (per the ADR-0035 singleton `NpgsqlDataSource`, it is per-txn not per-connection), policy `tenant = current_setting(...)`. Keying primary, RLS backstop — the same defense-in-depth philosophy as the call filter.
- **Azure Table Storage has no RLS equivalent.** Isolation there is *purely* the partition key plus always-partition-scoped queries. There is no second wall the storage engine enforces, so on Azure the keying + the call filter + the conformance battery are the *sole* controls.

This asymmetry is a **documented, accepted limitation**, not papered over. It is why the Azure conformance scenarios carry extra weight (§10): they are the only proof Azure has. Azure is *not* held to a higher bar (per-tenant storage account/table) for v1.

---

## 9. Single-tenant pays nothing (with the key-shape asterisk)

Registering nothing leaves tenancy off and free: no relay seed, no call filter, no analyzer firing (it is selective on `[EdictTenantScoped]`, which a single-tenant app never applies), the tenant field a single null on the wire (as the principal field already is when auditing is off). The asterisk is the R1 one-way door: at runtime single-tenant pays nothing, but the *grain key is string-shaped for everyone*. That is the one structural effect, and it is invisible (§5).

Fail-closed on "tenant-scoped aggregate, tenant missing" is non-negotiable: `EdictMissingTenantException` at the origin, never a silent fall-through to a default partition — the silent fall-through *is* the breach.

---

## 10. The security centre of gravity: the key is the attack surface

This feature's highest-consequence bug class is a *silent* cross-tenant leak, and the string-prefix key (R1) *creates* a specific vulnerability that the rest of the design must close. This section is the one to get right.

### The delimiter-injection vector

`Compose(tenant, routeGuid) = $"{tenant}|{routeGuid}"`, and the call filter parses it back by splitting on `|`. The route-Guid side is safe (fixed 36-char `D` format, no `|`). **The tenant side is the attack surface.** If a tenant id can contain `|`, an attacker who influences their tenant string can forge another tenant's key space — `tenant = "globex|0000…0000"` composed with their own guid yields a string a naive parse splits in the attacker's favour, landing them in `globex`'s partition.

### The five hard requirements (R11)

1. **`EdictTenantId` charset is a security control.** A strict whitelist that **excludes the key delimiter `|`, the claim-check path separator `/`, and every char reserved by Azure Table partition keys (`\ # ?` + control) and Postgres** — the *intersection* of safe-everywhere, because the tenant flows into grain key, stream key, blob path, and SQL. Rejected at `Of()`, fail-closed, before it ever reaches `Compose`.
2. **Compose/parse is unambiguous and property-tested.** With `|` banned in the tenant and absent from the Guid, exactly one delimiter exists; parse takes everything before the first `|` as tenant and validates the suffix is a well-formed Guid, throwing on anomaly. A **property test** asserts Compose-then-parse recovers the exact tenant for *all* valid tenant ids — and it gates the build, the same way conformance does.
3. **Adversarial conformance, not just happy denial.** The stolen-key attack is an explicit battery: B obtains A's raw route key and hammers *every* entry point — direct command send, projection read, claim-check fetch, dead-letter — each denying, on **real** Azure Table + Postgres + Queue + Kafka.
4. **A generator-coverage guard** in `Edict.Architecture.Tests` asserts every tenant-scoped emit site routes through `Compose`, so a future generator refactor cannot silently drop one of the three sites.
5. **Fail-closed proven, never defaulted.** Missing/empty tenant on a tenant-scoped aggregate throws and *never* falls back to a shared/default partition — proven by test, because a silent default is a leak wearing a green checkmark.

---

## 11. Telemetry (R10)

The happy path is **silent** (the call filter passing emits nothing — correct; tenancy is not a per-operation event a consumer watches). Only a **denial** and a **privileged crossing** need to be seen, and there is a cardinality trap ADR-0039 exists to catch: **tenant id is unbounded and must never be a meter tag.**

- **Meter:** one bounded `cross_tenant_denied` counter, tagged only with a compile-closed reason, never the tenant value.
- **Span:** the denial and the privileged crossing get a span event on the `"Edict"` source; *there* the tenant value is acceptable (spans tolerate high cardinality), so an operator can trace an incident to its tenant.
- The privileged crossing also lands as an **audit row** (it is an attributable act) — no new mechanism.

---

## 12. The auth story (costed, serverless, consumer-side)

None of this is an Edict concern; Edict depends on the resolved `EdictTenantId`, nothing upstream. Recorded so the design has a concrete answer to "where does the authenticated identity come from."

| Customer shape | Use | Authenticated claim | Cost at this scale | Own server? |
|---|---|---|---|---|
| Consumers / self-service (B2C/CIAM) | Microsoft Entra External ID | a signed custom extension claim | Free up to 50,000 MAU | No |
| Enterprise each with their own Entra (B2B) | Multi-tenant Entra app (`organizations`/`common`) | the built-in `tid` GUID | Effectively free | No |

Both put the claim inside an IdP-signed JWT verified against the IdP's JWKS, so a client cannot forge it. A tenant value in a header, subdomain, or unsigned cookie is client-supplied and must never gate isolation. Remember the §4 indirection: the signed claim authenticates the *principal*; the consumer maps it to the *tenant*.

Rejected briefly: Azure AD B2C (P2 discontinued March 2026); self-hosted Keycloak/Zitadel/Ory (violate "no own server"); Auth0 B2B (~$800/mo past free); Clerk / Zitadel Cloud (~$100/mo) are cheaper managed second choices off the Microsoft stack but neither beats free-and-native.

---

## 13. Surfaces touched (grill capture)

| Axis | Requirement |
|---|---|
| **Vocabulary** (`CONTEXT.md`) (R7) | New terms: **Tenant**, **Tenant Id**, **Tenant-Scoped Aggregate**, **Public Aggregate**, **Company Wall**, **Establishing Crossing**. Sharpest `_Avoid_`: "customer" (ambiguous in a B2C+B2B book). Written when the first slice lands, not now (MCP reads it as ground truth). |
| **ADRs** (R8) | **Two**: (a) *Tenant as a routed identity axis* (uniform string keys, typed route key + `[EdictTenantScoped]`, `Compose`, the one-way door), (b) *Tenant isolation enforcement and storage* (fail-closed carry, call filter, explicit crossings, honour-side folding, RLS / Azure asymmetry). Carry reuse earns no ADR (it references ADR-0063). Numbers scanned at write time (~0065/0066). |
| **Wire shape / breaking** (R9) | Additive: `Tenant` field, the new public types, the analyzer. **Breaking** (taken now, `Breaking:` line, `feat!:` + `BREAKING CHANGE:`, Verify regen): dispatch interfaces flip to `IGrainWithStringKey` (persisted key shape changes); `[EdictRouteKey]` widens to a typed key; the composed key format. |
| **Skills** (5 bodies) | `edict-contracts` (typed route key + `[EdictTenantScoped]` + charset rule), `edict-silo-wiring` (`AddEdictTenant`), `edict-authoring` (tenant-scoped vs public aggregate + establishing crossing), `edict-testing` (the run-as-tenant seam), `edict-diagnostics` (`EdictCrossTenantAccessException`, `EdictMissingTenantException`, the new analyzer). |
| **MCP** | No new tool — extend `edict_list_route_keys` / `edict_list_handlers` with a tenant-scoped flag; new terms/ADRs flow through the existing glossary/ADR tools. |
| **Testing seam** (`Edict.Testing`, built) | A `RunAsTenant(EdictTenantId)` seam on `EdictTestApp` that seeds the relay, so consumers drive "as Acme" and assert denial deterministically. |
| **Drift guards** | AgenticTooling interlock for the new analyzer id + glossary terms referenced by skills; Architecture.Tests allow-list additions + the recorded breaking dispatch-interface change + the new generator-`Compose`-coverage guard. |
| **Docs** | New `docs/usage/concepts/multi-tenancy.md` (lead with the company-wall vs within-tenant-authz split); `wiring/postgres.md` (RLS); `wiring/azure-persistence.md` (the no-second-wall asymmetry, openly). |
| **Conformance** (R11, the deliverable) | **Streaming axis:** the string-key-stream routing proof (Slice 0 graduated), bound on Azure Queue + Kafka. **Persistence axis:** A-writes/B-empty for grain state, List read, claim-check fetch, dead-letter tagging, bound on Azure Table + Postgres, with a Postgres-only RLS-backstop assertion. The call-filter denial is a `Edict.Core` unit test (substrate-agnostic), recorded as a deliberate non-conformance. `Edict.Pairing.Tests` gains a tenant round-trip per pairing. |
| **README** | A multi-tenancy capability section with the honest Edict-vs-consumer responsibility split. |
| **Sample** (R12) | Additive B2B `Company`/`Employee` section in shared `Sample.Contracts`/`Sample.Domain` (`[EdictTenantScoped] CompanyId`), surfaced in **both** webs (Azure + KafkaPostgres parity). The public "register your company" page is the establishing crossing on screen. A **tenant switcher** ("acting as: [Acme ▾]") stubs the signed-tenant seam so a visitor flips Acme→Globex and watches the employee list change. **Each demo company seeded with 5–20 distinct employees** so the switch shows populated→*different*-populated, not empty. Existing orders stay untouched = the live public-aggregate proof. |
| **Sample audit page** (R13) | The audit record gains `Tenant` as a first-class field (parallel to `Principal`, same origin stamp; Postgres column under RLS). The audit **read is ambient-tenant-scoped** (a tenant sees its own trail, like reads) with a **privileged operator superset**. The Sample audit page shows a Public/Tenant column under the switcher plus an "(operator)" toggle for the full cross-tenant table. |

---

## 14. Non-goals

- **Edict will not authenticate.** The edge authenticates; Edict carries and enforces an already-trusted, consumer-mapped tenant.
- **Edict will not depend on an IdP SDK.** No `Microsoft.Identity.Web`, no `ClaimsPrincipal` in `Edict.Contracts`; the resolver `Func` is the only contact.
- **Edict will not do within-tenant authorization.** Which user may touch which employee or field is the consumer's, on the principal axis.
- **No tenant-migration / re-keying tooling in v1.**
- **No tenant-facing dead-letter read in v1** (operator-scoped + tagged; deferred).
- **Per-tenant physical deployment** (one substrate per tenant) is the other isolation model, possible today with zero framework change, and remains the recommendation for the highest-risk regulated tenants. This doc is the pooled-compute model.
- **GDPR data-subject erasure** stays deferred (the third identity axis).

---

## 15. Suggested first slices

**Slice 0 (spike, prerequisite):** prove R1 mechanically. One string-keyed command grain, one implicitly-subscribed string-keyed projection, an event routed through `StreamId.Create(name, "{tenant}|{guid}")`, asserting the projection grain activates with the string key intact. Throwaway; graduates into the streaming conformance axis.

**Slice 1 (carry):** `EdictTenantId` (charset-validated, §10), the `Tenant` field beside `Principal`, `AddEdictTenant(resolver)` mirroring `AddEdictAudit`, the unified `OriginIdentity.Seed(principal, tenant)`, fail-closed at origin. Proves a tenant surviving an edge send, a saga `Dispatch`, and a schedule fire.

**Slice 2 (keying, one surface):** the typed route key + `[EdictTenantScoped]` (R2), the `Compose` seam through the command grain key + stream key + projection partition, the new analyzer (R3), the call filter (R4) and `EdictCrossTenantAccessException`; one substrate (Postgres), one surface (a List projection, tenant-as-partition). The headline read then its denial.

**Slice 3+ (honour, per surface/substrate):** grain state, claim-check (mandatory fold), dead-letter (operator-scoped + tagged), the second substrate, Postgres RLS, the §10 security battery (charset property test, stolen-key adversarial conformance, generator-coverage guard).

**Slice N (demonstration):** the Sample B2B section + tenant switcher + seeded data + the audit page (R12/R13), the docs page, the README section.

The cross-tenant isolation battery (write as A, assert B's identical-key read is empty, on every surface, on every substrate, plus the adversarial stolen-key sweep) is the real deliverable; the base classes are the easy part.

---

## Sources

- **Orleans keying / streaming (the load-bearing facts in §5):** `DefaultStreamIdMapper.cs`, `GrainIdKeyExtensions.cs` on `github.com/dotnet/orleans` (`main`); grain identity / placement / streaming pages on learn.microsoft.com/dotnet/orleans; `Orleans.Multitenant` (v4 on Orleans 10 / .NET 10) for the string-key-prefix prior art.
- **Auth landscape:** Microsoft Entra External ID overview and pricing; multi-tenant app + claims validation; Azure AD B2C deprecation FAQ, on learn.microsoft.com/entra.
- **The shipped carry it reuses:** ADR-0063 (principal), ADR-0064 (audit/GDPR), and the `EdictPrincipal` / `PrincipalRelay` / `EdictPrincipalStamper` / `OriginSendPrincipalAnalyzer` / `SendAsync(command, EdictPrincipal)` code.
- **Edict internals (§3, §5, §8):** `EdictSender.cs`, `PublishEventExecutor.cs`, `EdictListProjectionBuilder.cs`, `IEdictCommandHandler.cs`, the `IGrainWith*Key` dispatch interfaces, and the three route/stream/projection generator emitters.
