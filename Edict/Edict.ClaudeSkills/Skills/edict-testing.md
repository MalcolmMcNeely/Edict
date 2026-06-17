---
name: edict-testing
description: Use this skill when working on a consumer app built on Edict and writing tests against an Edict consumer app — anything spinning up EdictTestApp, asserting on the Timeline, probing a saga or projection, or swapping a consumer-injected collaborator. Covers the Edict.Testing in-memory harness in full.
---

# Testing an Edict consumer app

Tests against an Edict consumer ride on the shipped `Edict.Testing` package. The harness boots the consumer's grains on an in-memory Orleans cluster with the real Outbox/saga engine, memory streams, an in-memory single store, and a virtual `TimeProvider`. Consumer code behaves identically under test and in production. Chaos delivery is on by default and not configurable.

Reach for `EdictTestApp` in every consumer test. Do not mock Orleans, do not mock `IEdictSender`, do not stub out a Saga, a Projection Builder, or a Command Handler — those are the code under test.

## Smallest valid test

```csharp
await using var app = await EdictTestApp.StartAsync(b => b
    .WithConsumer(typeof(OrderCommandHandler).Assembly));

await app.SendAsync(new PlaceOrderCommand(orderId, "REF-001"));
await app.Drain();

await Verify(app.Timeline);
```

The `Timeline` is the deterministic Verify-shaped view of every Command sent, Event raised, and consumer Invocation observed. Volatile envelope fields (ids, timestamps, W3C trace context) are scrubbed; the snapshot is the wire-format drift guard.

A **state-only Command** — one that mutates the aggregate's `State` and raises no Event — has no Event on the `Timeline` of its own. Assert it through its *downstream* effect, not its private grain `State`: send the accumulating Commands, then send the Command that reads the accumulated state and raises an Event, and `Verify` that the Timeline shows the state-only Commands with no following Event plus the later Event whose payload was derived from what they accumulated. Reach a projection it drives with the probe that matches the projection's species (`GetProjectionRow` for a List projection, `ReadProjectionAsync` for a State projection). Do not add a probe to read the grain's `State` directly — the observable surfaces are the contract.

**Read-your-writes is a flow assertion, not a cursor seam.** `EdictTestApp` has no cursor or timeout probe, by design: `Drain()` settles the whole chain, so after it the in-grain read model is already committed and `ReadProjectionAsync` reads it the same way it reads any State projection. There is nothing to wait on, so a test never passes an `EdictCursor` or a timeout. Assert read-your-writes as the flow it is — `SendAsync` the Command, `Drain()`, then `ReadProjectionAsync` shows the read model the Command set in motion. The cursor wait itself (the bounded timeout against the virtual clock, the `CursorReached`/`CursorTimedOut` tri-state, ring eviction, correlation propagation) is framework mechanism, unit-tested inside `Edict.Core.Tests`, not consumer surface.

## Probing a projection: pick the species

A projection has two species, and each has its own read probe. Reach for the one that matches the builder under test:

- A **List projection** (`EdictListProjectionBuilder<TRow>`) writes rows to a table. Read one with `GetProjectionRow<TRow>(tableName, partitionKey, rowKey)`. A State projection has no table row, so `GetProjectionRow` will never find it.
- A **State projection** (`EdictProjectionBuilder<TProjection>`) commits a single forward-only read model in-grain, addressed by routing key. Read it with `ReadProjectionAsync<TProjection>(key)`, which resolves the same `IEdictProjectionReader<TProjection>` seam the application tier binds to. A List projection is not addressed by one routing key, so `ReadProjectionAsync` is not its read.

`Drain()` first either way; the cursorless read then answers immediately.

## The EdictTestApp surface

- **`EdictTestApp.StartAsync(configure)`** — boots the in-memory cluster. The `configure` callback uses `EdictTestAppBuilder`.
- **`EdictTestAppBuilder.WithConsumer(Assembly)`** — required. The consumer grain assembly whose `AddEdict()` and generator-emitted route map the cluster boots.
- **`EdictTestAppBuilder.Replace<TService>(fake)`** — registers `fake` as the resolved `TService` on both silo and client containers. Use this to swap a consumer-injected collaborator (an `IEmailNotifier`, an HTTP client wrapper, a tenant lookup). Last-`AddSingleton`-wins, so the fake takes precedence. **Grain implementations are not swappable through this seam** — they are framework-owned.
- **`EdictTestApp.SendAsync(EdictCommand)`** — issues a Command through the real `IEdictSender` decorated for timeline recording. This is the in-memory swap of the production `IEdictSender` — the same seam consumers inject in production code. A validator-driven `Rejected` flows back through this same call, so assert validator behaviour by `SendAsync`-ing a payload the validator rejects and inspecting the returned `EdictCommandResult` (or the `Timeline` for the recorded `Rejected` outcome).
- **`EdictTestApp.Timeline`** — the recorded sequence to `Verify` against. The default assertion shape for any workflow with more than one observable step.
- **`EdictTestApp.GetSagaProgress<TSaga, TProgress>(Guid key)`** — typed read of the saga grain's durable `Progress` for direct snapshot assertion.
- **`EdictTestApp.GetProjectionRow<TRow>(tableName, partitionKey, rowKey)`** — typed read of the row a `EdictListProjectionBuilder<TRow>` last wrote. The List-species probe.
- **`EdictTestApp.ReadProjectionAsync<TProjection>(key)`** — typed read of the in-grain read model an `EdictProjectionBuilder<TProjection>` committed for the routing `key`, resolved through the same `IEdictProjectionReader<TProjection>` seam the application tier binds to. The State-species probe; returns `null` when the projection's `HandleAsync` never ran for that key.
- **`EdictTestApp.GetOutboxState(grainType)`** — `(TotalPending, OldestEnqueuedAt)` the observable gauges would scrape. For metrics-shape tests.
- **`EdictTestApp.GetSagaState(sagaType)`** — most-recent `lastHandledAt` across sagas of that type on the silo. Pairs with `AdvanceClock` for idleness-shaped tests.
- **`EdictTestApp.Drain()`** — settles the engine. Returns when the inline outbox drain has run, the in-process publisher has fanned every event out, every cascading `SendCommand` has settled, and the chaos held-queue is empty. Hard timeout.
- **`EdictTestApp.AdvanceClock(TimeSpan)`** — advances the virtual `TimeProvider` (the engine's backoff/reminder gate) and drains. Backoff timing elapses with no wall-clock wait.
- **`EdictTestApp.FireDueSchedulesAsync()`** — drives the next round of due schedule fires. Reads the soonest due instant across every grain a Command has been routed to, advances the virtual clock to it, fires every grain now due, and drains so the fired outcome (raised Events, dispatched Commands) lands on the `Timeline`. A no-op when no schedule is active.
- **`EdictTestApp.FireScheduleTimeoutsAsync()`** — the symmetric seam for the timeout cap. Advances to the soonest cap instant, fires the timeout on every grain at or past its cap, and drains so the compensation (`OnScheduleTimeoutAsync`) or the dead-letter (when no hook is written) lands on the `Timeline`.
- **`EdictTestAppBuilder.WithAudit()`** — turns auditing on, backed by in-memory audit stores so no container is needed. Sends are attributed to a default principal so simply turning it on never trips the origin fail-closed.
- **`EdictTestApp.ActAs(EdictPrincipal)`** — attributes every subsequent audited send to that actor. Call it before `SendAsync`; absent any call, sends carry the default test principal.
- **`EdictTestApp.Audit`** — the consumer read surface (`IEdictAuditRepository`) over the captured chain: `ByEntityAsync` / `ByCorrelationAsync` / `ByPrincipalAsync`, `VerifyEntityChainAsync`, and `GetPayloadAsync`. Available only after `WithAudit()`.
- **`EdictTestApp.TamperWithAuditRecord(EdictAuditRecord)`** — rewrites a stored record in place (the one mutation a production WORM store refuses) so a test can prove `VerifyEntityChainAsync` catches an altered chain.
- **`EdictTestAppBuilder.WithTenancy()`** — turns multi-tenancy on, wiring the test's ambient tenant resolver (`AddEdictTenant`) and the isolation backstop on both silo and client. Required before any of the tenant seams below; absent it they throw "Tenancy is off".
- **`EdictTestApp.RunAsTenant(EdictTenantId)`** — sets the ambient tenant for every subsequent send and read, the deterministic "act as Acme" seam. Re-read per send and per read, so a single test can switch walls by calling it again.
- **`EdictTestApp.SendAsync(EdictCommand, EdictTenantId)`** — the establishing-crossing overload: stamps the tenant onto the command directly, for the public-to-tenant onboarding send a fresh tenant has no ambient context for yet.
- **`EdictTestApp.QueryMyTenantPartitionAsync<TListProjection>()`** — reads the ambient tenant's own partition of a tenant-scoped List projection (the `IEdictTenantScopedListProjectionReader` surface). Drains first; a different `RunAsTenant` makes the identical call return empty.
- **`EdictTestApp.TenantAudit`** — the `IEdictTenantScopedAuditRepository` scoped to the ambient tenant, so a test asserts a business sees only its own trail.

## Testing a schedule (interval-agnostic)

Both fire seams read the schedule's own next-due (or next-timeout) instant from the grain and advance the virtual clock to exactly that point, so a test never names the cadence. Do **not** `AdvanceClock(TimeSpan.FromSeconds(2))` to match a `Schedule(every: 2s)` call: that couples the test to a literal the handler owns, and the test breaks the moment the cadence changes even though behaviour is identical. Call `FireDueSchedulesAsync()` once per fire and chain it to walk a multi-step scheduled workflow:

```csharp
await app.SendAsync(new StartFulfillmentCommand(orderId, lineIds));
await app.Drain();

await app.FireDueSchedulesAsync();   // first line fulfilled
await app.FireDueSchedulesAsync();   // second line fulfilled, schedule completes

await Verify(app.Timeline);
```

Assert the compensation and dead-letter branches the same way: drive the schedule with `FireScheduleTimeoutsAsync()` and `Verify` the `Timeline`, which shows the `OnScheduleTimeoutAsync` outcome (a compensating Command or raised Event) or the dead-letter row. Swap any collaborator a fire handler resolves (an `IWarehouseGateway`, a gateway client) through the same `Replace<TService>` seam used elsewhere; a schedule fire handler is ordinary handler code and composes with DI identically.

## Testing audit capture

Turn auditing on with `WithAudit()` and the harness captures decisions to in-memory stores: one C1 record per Command decision (including a validator `Rejected`) and one E1 record per raised Event, on a per-aggregate tamper-evident chain. `Drain()` settles the audit drain along with everything else, so there is nothing to poll: assert through `Audit` straight after.

```csharp
await using var app = await EdictTestApp.StartAsync(b => b
    .WithConsumer(typeof(OrderCommandHandler).Assembly)
    .WithAudit());

app.ActAs(EdictPrincipal.Of("clerk-7"));
await app.SendAsync(new PlaceOrderCommand(orderId, "REF-001"));
await app.Drain();

var records = await app.Audit.ByEntityAsync(typeof(OrderCommandHandler).FullName!, orderId.ToString());
// records[0] is the C1 command decision, records[1] the E1 OrderPlaced event,
// both attributed to clerk-7.
```

Assert the chain is unaltered, then prove the verifier bites by tampering:

```csharp
var verdict = await app.Audit.VerifyEntityChainAsync(entityType, entityKey);
// verdict.IsIntact is true.

var eventRecord = records.Single(record => record.Kind == EdictAuditKind.Event);
app.TamperWithAuditRecord(eventRecord with { MessageType = "Tampered.Event" });
var broken = await app.Audit.VerifyEntityChainAsync(entityType, entityKey);
// broken.IsIntact is false; broken.BrokenAtSequence names the altered record.
```

Retrieve a captured body with `GetPayloadAsync(record.RecordId)`; its bytes hash to the record's `PayloadHash`. Assert audit through these external surfaces (the queryable record, the chain verdict, the retrieved payload), never a private field or a capture count.

## Testing the tenant wall

Turn tenancy on with `WithTenancy()`, then drive "act as a company" with `RunAsTenant`. The wall is **structural**, so the proof is a deterministic seam, not a wall-clock wait: switching the ambient tenant swaps the visible rows, and a cross-tenant read is empty by construction.

```csharp
await using var app = await EdictTestApp.StartAsync(b => b
    .WithConsumer(typeof(EmployeeCommandHandler).Assembly)
    .WithTenancy());

// Acme onboards (the one explicit establishing crossing) and adds an employee.
await app.SendAsync(new RegisterCompanyCommand(acmeAdminId, "Acme"), EdictTenantId.Of("acme"));
app.RunAsTenant(EdictTenantId.Of("acme"));
await app.SendAsync(new AddEmployeeCommand(new EmployeeId(Guid.NewGuid()), "Ada"));
await app.Drain();

// Acme sees its own employee.
var acmeRows = await app.QueryMyTenantPartitionAsync<EmployeeDirectoryRow>();
Assert.Single(acmeRows);

// Globex, switched in on the same app, sees an empty list — not a permission error, structurally empty.
app.RunAsTenant(EdictTenantId.Of("globex"));
var globexRows = await app.QueryMyTenantPartitionAsync<EmployeeDirectoryRow>();
Assert.Empty(globexRows);
```

The same shape proves the audit wall: after `RunAsTenant`, `TenantAudit.ByEntityAsync(...)` returns only the ambient tenant's records. A test asserts the wall through these observable surfaces (the swapped partition, the empty cross-tenant read, the scoped audit trail), never by inspecting a composed key. Prior art for a full Sample-level integration assertion is `Sample.Azure.Silo.Tests`.

## Chaos is on by default

`Edict.Testing` applies bounded duplicate redelivery and bounded reorder to every published event on every test run. There is no `WithoutChaos`, no seed override, no per-test escape hatch. Chaos models the at-least-once production contract; if a test is order-sensitive, it is asserting on a stricter contract than Edict guarantees in production — fix the consumer, not the harness.

`Drain` releases held-queue events on its own stability ticks, so no `Task.Delay` is needed (or wanted) anywhere in test code. If a test reaches for `Task.Delay`, replace it with `await app.Drain()` or `await app.AdvanceClock(...)`.

## What is real and what is bypassed

| Mechanism | Under `Edict.Testing` |
| --- | --- |
| `EdictIdempotencyBase` dedup ring | **Real.** Chaos duplicates are suppressed by the consumer's grain exactly as in production. |
| Outbox drain engine | **Real.** Same `OutboxHost`, same slice transitions, same dead-letter promotion. |
| `EdictDeadLetterProjectionBuilder` | **Real.** Dead-letter rows land in the in-memory table store and can be read via `GetProjectionRow` against the `"deadletter"` table. |
| Grain persistence | **In-memory.** `AddMemoryGrainStorage("edict-state")`. |
| Stream hop | **Bypassed.** The in-process publisher dispatches synchronously through `IEdictEventConsumer`; the memory-stream pulling agent is not exercised. |
| Wire serialisation | **Real.** Events round-trip through the same MessagePack pipeline production uses. |
| Trace / W3C continuity | **Real.** Spans open per publish and per invocation, with production's per-turn topology: an inline drain nests the publish under the staging command, a recovery drain (no live event reference) makes it its own root linking back. |
| Claim check | **In-memory dictionary.** Same threshold, same envelope shape. |
| Audit capture + chain | **Real, in-memory store.** Same C1/E1 capture, same per-aggregate hash chain and verification; the WORM store is an in-memory dictionary (off unless `WithAudit()`). |
| `TimeProvider` | **Virtual.** `FakeTimeProvider` advanced via `AdvanceClock`. |

## See also

- For the role bound to the code under test: see the `edict-authoring` skill.
- For the contract attributes the test exercises: see the `edict-contracts` skill.
- For investigating dead-letter rows surfaced by `Drain`: see the `edict-diagnostics` skill.
