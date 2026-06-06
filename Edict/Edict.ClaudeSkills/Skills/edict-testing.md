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

A **state-only Command** — one that mutates the aggregate's `State` and raises no Event — has no Event on the `Timeline` of its own. Assert it through its *downstream* effect, not its private grain `State`: send the accumulating Commands, then send the Command that reads the accumulated state and raises an Event, and `Verify` that the Timeline shows the state-only Commands with no following Event plus the later Event whose payload was derived from what they accumulated. Reach a projection it drives with `GetProjectionRow`. Do not add a probe to read the grain's `State` directly — the observable surfaces are the contract.

**Read-your-writes is a flow assertion, not a cursor seam.** `EdictTestApp` has no cursor or timeout probe, by design: `Drain()` settles the whole chain, so after it the projection row is already there and `GetProjectionRow` reads it the same way it reads any projection. There is nothing to wait on, so a test never passes an `EdictCursor` or a timeout. Assert read-your-writes as the flow it is — `SendAsync` the Command, `Drain()`, then `GetProjectionRow` shows the row the Command set in motion. The cursor wait itself (the bounded timeout against the virtual clock, the `CursorReached`/`CursorTimedOut` tri-state, ring eviction, correlation propagation) is framework mechanism, unit-tested inside `Edict.Core.Tests`, not consumer surface.

## The EdictTestApp surface

- **`EdictTestApp.StartAsync(configure)`** — boots the in-memory cluster. The `configure` callback uses `EdictTestAppBuilder`.
- **`EdictTestAppBuilder.WithConsumer(Assembly)`** — required. The consumer grain assembly whose `AddEdict()` and generator-emitted route map the cluster boots.
- **`EdictTestAppBuilder.Replace<TService>(fake)`** — registers `fake` as the resolved `TService` on both silo and client containers. Use this to swap a consumer-injected collaborator (an `IEmailNotifier`, an HTTP client wrapper, a tenant lookup). Last-`AddSingleton`-wins, so the fake takes precedence. **Grain implementations are not swappable through this seam** — they are framework-owned.
- **`EdictTestApp.SendAsync(EdictCommand)`** — issues a Command through the real `IEdictSender` decorated for timeline recording. This is the in-memory swap of the production `IEdictSender` — the same seam consumers inject in production code. A validator-driven `Rejected` flows back through this same call, so assert validator behaviour by `SendAsync`-ing a payload the validator rejects and inspecting the returned `EdictCommandResult` (or the `Timeline` for the recorded `Rejected` outcome).
- **`EdictTestApp.Timeline`** — the recorded sequence to `Verify` against. The default assertion shape for any workflow with more than one observable step.
- **`EdictTestApp.GetSagaProgress<TSaga, TProgress>(Guid key)`** — typed read of the saga grain's durable `Progress` for direct snapshot assertion.
- **`EdictTestApp.GetProjectionRow<TRow>(tableName, partitionKey, rowKey)`** — typed read of the row a `EdictListProjectionBuilder<TRow>` last wrote.
- **`EdictTestApp.GetOutboxState(grainType)`** — `(TotalPending, OldestEnqueuedAt)` the observable gauges would scrape. For metrics-shape tests.
- **`EdictTestApp.GetSagaState(sagaType)`** — most-recent `lastHandledAt` across sagas of that type on the silo. Pairs with `AdvanceClock` for idleness-shaped tests.
- **`EdictTestApp.Drain()`** — settles the engine. Returns when the inline outbox drain has run, the in-process publisher has fanned every event out, every cascading `SendCommand` has settled, and the chaos held-queue is empty. Hard timeout.
- **`EdictTestApp.AdvanceClock(TimeSpan)`** — advances the virtual `TimeProvider` (the engine's backoff/reminder gate) and drains. Backoff timing elapses with no wall-clock wait.
- **`EdictTestApp.FireDueSchedulesAsync()`** — drives the next round of due schedule fires. Reads the soonest due instant across every grain a Command has been routed to, advances the virtual clock to it, fires every grain now due, and drains so the fired outcome (raised Events, dispatched Commands) lands on the `Timeline`. A no-op when no schedule is active.
- **`EdictTestApp.FireScheduleTimeoutsAsync()`** — the symmetric seam for the timeout cap. Advances to the soonest cap instant, fires the timeout on every grain at or past its cap, and drains so the compensation (`OnScheduleTimeoutAsync`) or the dead-letter (when no hook is written) lands on the `Timeline`.

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
| `TimeProvider` | **Virtual.** `FakeTimeProvider` advanced via `AdvanceClock`. |

## See also

- For the role bound to the code under test: see the `edict-authoring` skill.
- For the contract attributes the test exercises: see the `edict-contracts` skill.
- For investigating dead-letter rows surfaced by `Drain`: see the `edict-diagnostics` skill.
