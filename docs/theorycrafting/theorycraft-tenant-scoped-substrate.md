# Theorycraft — tenant-scoped substrate (multi-tenant isolation)

**Status:** pre-design / theorycraft. Not a PRD, not a spike, not an ADR. Goal: a fresh session (Claude or human) can pick it up cold and start a design pass without re-deriving the problem statement.

**Sibling docs:**
- [`theorycraft-multi-tenant-identity-and-surface.md`](theorycraft-multi-tenant-identity-and-surface.md) — the **carry side** of this same feature: the auth story (Entra External ID / multi-tenant Entra), the consumer-facing public surface (`WithTenancy()`, `AddEdictTenancy`, the resolver seam), and the edge-to-saga propagation loop. This doc is the **honour side** (storage scoping). They share three locked decisions (envelope-not-ambient, fail-closed, opt-in wiring) and have one open disagreement to reconcile in the ADR: the tenant-id type (Open Question 2 here vs that doc's Open Question 4).
- [`theorycraft-keyed-projection-builder.md`](theorycraft-keyed-projection-builder.md) — interacts: a tenant scope must reach keyed-PB grain state the same way it reaches command-handler aggregate state.
- [`theorycraft-projection-claim-check.md`](theorycraft-projection-claim-check.md) — interacts: claim-check blobs/rows are one of the storage surfaces a tenant scope has to cover, or oversized payloads leak across the boundary while inline rows do not.
- [`theorycraft-read-your-writes.md`](theorycraft-read-your-writes.md) — interacts: the cursor wait-list is per-grain, so it is already tenant-isolated if the grain key is; no extra work, but worth confirming during design.

## The problem

A consumer wants one Edict deployment (one Orleans cluster, one substrate) to serve multiple customers. The hard constraint is regulatory: one customer must never be able to read or even observe another customer's data. A leak is not a bug, it is a compliance breach with real fines attached.

Edict has no answer for this today, and the gap is structural, not cosmetic. The entire identity model is **one `Guid`**: the `[EdictRouteKey]`. That single value is the command-handler grain key, the stream key (`StreamId.Create(streamName, routeKey)` in `Edict.Core/Outbox/PublishEventExecutor.cs`), and, via implicit subscription, the projection/saga grain key as well. The only other addressing dimension is the `[EdictStream]` namespace string. There is no ambient context, no per-caller scope, nothing flowing alongside the route key that storage could key on.

The consequence is that every framework-owned storage surface pools all tenants together by default:

| Surface | What controls placement today | Tenant marker today |
|---|---|---|
| Grain state (Postgres) | composite PK `(grain_type, grain_id, state_name, service_id)` in `EdictPostgresGrainStorage` | none (`service_id` is silo-wide) |
| Grain state (Azure) | native Orleans blob naming | none |
| Table projection rows | `TableName` (literal per builder), `PartitionKey` (overridable, defaults to grain key), `RowKey` (overridable) | author-controlled, none by default |
| Claim-check (Azure) | blob path `yyyy/MM/dd/{guid}` in `AzureBlobClaimCheckStore` | none |
| Claim-check (Postgres) | UUID primary key in `PostgresClaimCheckStore` | none |
| Dead-letter | partition collapses to the literal `"deadletter"`, row = entry id, **payload bytes included** | none, every tenant pooled |

The dead-letter row is the sharpest illustration: `EdictDeadLetterProjectionBuilder` deliberately writes every failure into a single `"deadletter"` partition for fleet-wide reads, and the row carries `PayloadJson` plus source metadata. It is a cross-tenant data store sitting in the middle of the framework's safety net.

## The decision this primitive does not make for you

Tenant **authentication** is the consumer's domain and stays there. The tenant identity comes from the authenticated principal at the Web/API edge. Edict cannot and should not invent it, and must never trust a tenant value carried in a command payload without the edge having authenticated it first (confused-deputy risk: nothing stops a caller crafting a message for another tenant's scope otherwise).

What Edict *can* own is faithful **propagation and storage scoping** of an already-authenticated tenant claim. That is the gap, and it is a genuine framework capability, not something a consumer can bolt on without reaching into every provider.

## What the primitive does

A **tenant scope** is a value that travels with every command and event, and that every substrate provider honours when it places or reads data, so the store itself refuses cross-tenant access (defense in depth, not "every query is correct forever").

Two halves, mirroring read-your-writes:

**Carry side.** The tenant scope rides *in the message envelope*, the same way the route key already does. It is not ambient context. This is the single most important design decision in the doc and the reasoning is in Open Question 1.

**Honour side.** Each storage provider folds the tenant scope into its placement key:

- Grain state: an extra component on the Postgres composite PK; a path prefix on the Azure blob.
- Table projections: a tenant prefix on the partition key (or a per-tenant table name).
- Claim-check: a tenant prefix on the blob path / an extra PK column.
- Dead-letter: the literal `"deadletter"` partition becomes tenant-scoped.
- Streams: optionally a tenant component in the stream namespace, so even the in-flight stream is partitioned.

```csharp
// Edge (consumer code): tenant comes from the authenticated principal, not the request body
await sender.SendAsync(command, tenant: authenticatedPrincipal.TenantId);

// Everything downstream is scoped automatically: grain state, projection rows,
// claim-check blobs, dead-letter rows all land in the tenant's partition.
CustomerRow row = await repository.GetAsync<CustomersProjection, CustomerRow>(customerId);
// ^ resolves within the calling tenant's scope; cannot return another tenant's row
```

The win is shared compute with isolation enforced at the store. The cost is that *every* surface has to honour it or there is a silent leak, and "every surface" includes the dead-letter pool that is fleet-wide today.

## Why this is the right shape for Edict (and two wrong shapes to reject)

**Reject: tenant baked into the route key.** Make `[EdictRouteKey]` a deterministic UUID over `(tenant, local-id)` and tenants get different grains for free, with zero framework change. This is a fig leaf. Every tenant still shares the same physical tables, blobs, streams, and dead-letter pool. The key is opaque, so storage cannot enforce "this row belongs to tenant X." One projection query missing its filter, one misroute, anyone with storage credentials, and you leak. It is fine as a *complement* layered on top of real storage scoping, never as the boundary. Document it as such; do not let it masquerade as the feature.

**Reject: ambient tenant context (AsyncLocal / Orleans `RequestContext`).** Tempting, and wrong for Edict. Activity/trace context does not survive the Azure Queue stream hop (this is already a known gap, see ADR-0028 streaming notes and the trace-propagation handling in the deferred-dispatch entry builders). Tenant context would die at exactly the same boundary. Worse, implicit-subscription grains are activated purely from the stream id, so there is no ambient scope at the consumer end at all. Ambient context here is a silent-leak generator, and silent failure in generated/runtime plumbing is the highest-risk failure class in this codebase.

**Accept: tenant in the envelope.** Carrying the scope as data on the message fits Edict's existing philosophy exactly: everything is an explicit key, there is no ambient state, and the route key already proves the pattern works across the stream hop. The scope survives the hop because it is data, not context. Providers and generators consume it. This is the only version that can be made airtight.

## Open design questions

1. **Envelope vs ambient — confirm and lock.** Recommended: envelope. The whole design hinges on it (see above). The open part is *where* on the envelope: a new first-class field on `EdictCommand` / `EdictEvent` base, or a header-style dictionary, or a dedicated `EdictTenantScope` record threaded through `GrainEnvelope<TPayload>`. Recommend a single typed field, not a free dictionary, so the analyzer can enforce its presence.

2. **Tenant scope type.** `Guid`? Opaque `string`? A typed `EdictTenantId` wrapper? `Guid` matches the route-key precedent and is index-friendly on every substrate. A `string` is friendlier for human-readable tenant slugs but invites injection concerns in path/partition construction. Recommend a typed wrapper over `Guid` for v1, with the wire shape being the underlying `Guid`.

3. **Is tenant optional or mandatory?** Single-tenant deployments (the common case, the Sample app) should not pay a tenancy tax. Options: a sentinel "default tenant" when unset, or a deployment-level switch that makes tenant mandatory and fails closed. Recommend: opt-in at wiring time (`AddEdict(...).WithTenancy()`), and once on, the analyzer requires a tenant on every send and the providers fail closed if a scope is missing. Fail-closed is non-negotiable given the regulatory bar.

4. **Per-tenant table/container vs shared-with-prefix.** Two physical strategies per surface:
   - **Shared store, tenant prefix on the key.** One `customers` table, partition key `{tenant}:{key}`. Cheap, scales to many small tenants, but isolation is logical and a query bug can still cross it.
   - **Per-tenant store.** Table `customers_{tenant}`, container `claim-check-{tenant}`. Stronger isolation, per-tenant backup/erasure/residency, but blows up object counts and complicates provisioning.
   Likely answer is a per-surface policy with a sensible default, not one global choice. Recommend: shared-with-prefix as the default (pooling is the point), per-tenant store as a documented option for high-isolation tenants.

5. **Dead-letter pool.** The literal `"deadletter"` partition is the thorniest surface. Fleet-wide dead-letter reads are an operator feature (one query to see all poison). Tenant isolation breaks that. Options: tenant-scoped partition with an operator-only cross-tenant read path that is explicitly privileged; or keep a fleet view but redact payloads cross-tenant. Recommend: tenant-scoped partition by default, with operator cross-tenant reads gated behind a separate, audited surface. This needs its own ADR section because it changes a shipped behaviour.

6. **Stream namespace scoping.** Should the stream itself be tenant-partitioned (`StreamId.Create($"{tenant}/Orders", routeKey)`), or only the storage at rest? Partitioning the stream isolates in-flight data and lets per-tenant consumers scale independently, but multiplies stream/queue/partition counts and interacts with the Kafka partition-count and Azure queue-count options. Recommend: storage-at-rest scoping is mandatory; stream scoping is an optional second tier for tenants that need in-flight isolation or independent throughput.

7. **Postgres RLS as a second wall.** The Postgres substrate could add row-level security on `edict_grain_state`, projection tables, `deadletter`, and `edict_claim_check`, keyed off a session variable. Caveat: Edict pools a singleton `NpgsqlDataSource` (ADR-0035), so the tenant session var must be `SET LOCAL` inside each transaction, not once per connection. RLS only covers Postgres (Azure Table/Blob have no equivalent and lean on partition-prefix + scoped SAS), and it still requires the tenant scope to be threaded down to set the var, so it does not remove the core work. Recommend: ship it as defense-in-depth *after* the threading lands, not as the primary mechanism.

8. **Generator and analyzer support.** The generators emit route resolution, stream wiring, and outbox plumbing. All of it has to learn about the tenant scope. An analyzer (`EDICT0xx`) should flag a send without a tenant when tenancy is on. This is the highest-risk surface to get right because its bugs fail silently (a missed scope drops to the default partition with no error). Treat the generator/analyzer pair as the top test target.

9. **Telemetry.** A `tenant` dimension on spans and metrics is obviously useful, but high-cardinality tenant tags blow up metric cardinality. Note that `TenantId` already exists in the codebase only as a `[EdictTelemeterized]` payload property in test fixtures, never as routing. Recommend: tenant on spans (traces are sampled), tenant on metrics only behind a bounded allow-list or as an exemplar, never as an unbounded label.

## Constraints from existing decisions

- **Substrate seam (ADR-0030).** This is the lever. The tenant scope is consumed at the substrate boundary, so the seam is where most of the honour-side work lands. The seam already abstracts harness-to-backend; extend it to carry tenant rather than inventing a parallel seam.
- **ADR-0002 idempotency model.** Dedup is keyed on `EventId` per consumer grain. If grain keys become tenant-isolated, dedup is automatically per-tenant. Confirm no shared dedup structure exists across tenants.
- **ADR-0007 contracts boundary.** The tenant scope crosses the wire (it is on the envelope). It needs a stable MessagePack-friendly concrete shape, no `[Union]`. A `Guid`-backed typed wrapper satisfies this.
- **ADR-0018 dead-letter.** The dead-letter partition change touches shipped behaviour. The fleet-wide read is a documented operator affordance; scoping it needs an ADR amendment, not a silent change.
- **ADR-0020 claim-check.** Claim-check blob/row placement is a separate surface from inline rows. It is easy to scope the inline projection row and forget the spilled blob, which would leak exactly the *largest* payloads. Treat claim-check scoping as a first-class line item, not an afterthought.
- **ADR-0041 exception philosophy.** A missing-tenant-when-required condition is a wiring/representability fault. It should be a typed `Edict*` throw at the boundary that fails closed, classified and dead-lettered (or rejected at the edge), never a silent drop to a default partition.
- **ADR-0035 DataSource singleton.** Constrains the RLS approach (per-transaction `SET LOCAL`, see Open Question 7).
- **Naming convention (CONTEXT.md, brand prefix).** Any consumer-typed surface (`EdictTenantId`, `WithTenancy`, a `tenant:` parameter) is `Edict`-prefixed per ADR-0017.

## Substrate considerations

- **Azure.** Grain state is Azure Table (composite key, tenant component goes on the partition key or a key segment). Projection rows are Azure Table (tenant prefix on partition key). Claim-check is Azure Blob (tenant prefix on the blob path, optionally a per-tenant container with scoped SAS). No RLS equivalent; isolation is structural via naming + scoped credentials.
- **Postgres.** Grain state, projection tables, claim-check, and dead-letter are all SQL. Tenant column on the composite PK is the natural move, and RLS is available as a second wall (Open Question 7). Effectively unlimited room to add the dimension.
- **Kafka.** Streaming only. Tenant stream scoping (Open Question 6) interacts directly with the partition-count options and per-stream topology. The state side is whichever persistence substrate is paired.
- **AWS SQS + DynamoDB / NATS / Cosmos / Mongo (when shipped).** Each adds a state surface that must honour the scope before that substrate can claim multi-tenant support. The conformance battery is where this gets enforced uniformly.

## Failure modes to design for

1. **Missing tenant when tenancy is on.** Must fail closed: a typed throw at the boundary, never a silent fall-through to a default partition. This is the breach-prevention invariant; it deserves a dedicated conformance test per substrate.
2. **Tenant set but provider ignores it.** The silent-leak case. A provider that has not been taught the scope writes to the unscoped key and the data is visible cross-tenant. Mitigation: a conformance test that writes as tenant A and asserts tenant B's identical-key read returns nothing, run against *every* surface (grain state, projection, claim-check, dead-letter), on *every* substrate. This battery is the real deliverable; the base classes are the easy part.
3. **Claim-check blob unscoped while inline row is scoped.** Leaks the largest payloads specifically. Covered by making the cross-tenant read test send an oversized payload.
4. **Dead-letter cross-tenant read.** An operator reading the pool sees every tenant's payloads. Covered by the dead-letter scoping decision (Open Question 5) plus an audited operator path.
5. **Tenant forged in payload.** Out of scope for the framework to authenticate, but the framework must read the scope from the trusted envelope field set at the edge, not from arbitrary command data, so a forged value in a command body has nowhere to take effect.
6. **Tenant change mid-flow.** A saga or outbox effect raised under tenant A must stay tenant A through every async hop and retry. The scope is captured on the envelope at raise time, not re-derived downstream. Verify across the outbox drain and the dead-letter promoter.

## Non-goals — explicit

- **Tenant authentication.** The edge authenticates; the framework carries and enforces. Stated twice on purpose.
- **Per-tenant deployment automation.** Standing up a cluster/substrate per tenant is the *other* isolation model (physical isolation), already supported today with zero framework change because the substrate is configured per host. This doc is the pooled-compute model. The two are complementary; physical isolation stays the recommendation for the highest-risk tenants.
- **Cross-tenant analytics.** Aggregating across tenants is a separate, privileged read path, not a consumer surface.
- **Tenant-aware throttling / quota / billing.** Useful, separable, out of scope here.
- **Re-keying existing data on tenant change.** No tenant-migration tooling in v1.

## Where this lands in the code

Rough sketch — verify against current structure before designing:

- `Edict.Contracts` — `EdictTenantId` (or equivalent) wire type; the tenant field on the command/event envelope; the `SendAsync(..., tenant:)` surface on `IEdictSender`.
- `Edict.Core` — thread the scope through `GrainEnvelope<TPayload>`, the outbox entry, `PublishEventExecutor` stream addressing, and the dead-letter promoter so it survives every hop and retry.
- `Edict.Substrate` (ADR-0030 seam) — the scope reaches every provider through the seam, not via a parallel mechanism.
- `Edict.Postgres` — tenant component on the `edict_grain_state` PK, projection-table keys, `edict_claim_check`, and the `deadletter` table; optional RLS policies + per-transaction session var.
- `Edict.Azure.Persistence` / `Edict.Azure.Streaming` — tenant prefix on partition keys, claim-check blob paths, dead-letter partition; per-tenant container option with scoped SAS.
- `Edict.Generators` / `Edict.Analyzers` — tenant-aware route/stream/outbox emission; an `EDICT0xx` analyzer that requires a tenant on every send when tenancy is enabled.
- `Edict.Tests.Conformance` — the cross-tenant isolation battery (the real deliverable), per surface, per substrate.
- `Sample.*` — a two-tenant variant of the sample that demonstrates a tenant A query cannot see tenant B data.
- CONTEXT.md — glossary entry for **tenant scope**.
- New ADR — the envelope-not-ambient decision, the per-surface scoping policy, the dead-letter pool change, and the fail-closed invariant.

## Suggested first slice

Smallest thing that proves the design, and it should prove *isolation*, not ergonomics:

1. `EdictTenantId` in `Edict.Contracts` and a tenant field on the command/event envelope.
2. `SendAsync(..., tenant:)` carries it; `GrainEnvelope<TPayload>` and the outbox preserve it across the drain.
3. One substrate (Postgres, because the composite PK extension is the cleanest), one surface (table projection rows): fold tenant into the partition key.
4. One conformance test: send as tenant A, attempt the identical-key read as tenant B, assert empty. This single red-to-green test is the whole point of the slice.
5. Fail-closed: tenancy on + missing tenant throws a typed `Edict*` at the boundary.

That validates the envelope-carry mechanism and the isolation invariant against a real substrate on the highest-traffic surface. Grain state, claim-check, dead-letter, the second substrate, the analyzer, and stream scoping each follow as their own slices once the carry mechanism and the conformance pattern exist.

## Related work elsewhere

Worth scanning before designing — these are the well-trodden patterns under other names:

- **Postgres Row-Level Security multi-tenancy** — the canonical shared-table-with-policy model; directly informs Open Question 7.
- **Citus / shared-schema vs schema-per-tenant vs database-per-tenant** — the classic taxonomy for Open Question 4's three strategies.
- **Orleans multi-tenancy patterns** (community articles, the `[ImplicitStreamSubscription]` + keyed-grain approach) — closest to Edict's grain model; most apply the composite-key approach this doc rejects as a standalone boundary.
- **Akka multi-tenant sharding** — same actor-per-key shape with a tenant dimension on the entity id.
- **AWS SaaS Lens / "silo, pool, bridge" isolation models** — the vocabulary for framing pooled (this doc) vs physical (the non-goal) isolation to an auditor.

A worked example from the Postgres RLS and the "silo/pool/bridge" framing is worth fifteen minutes before the design ADR, because the regulatory stakes mean the isolation argument has to be defensible to a compliance reviewer, not just to an engineer.
