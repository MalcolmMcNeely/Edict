# Probes

A probe reads what a Saga, Projection Builder, or Outbox observably did, on a stable timeline. `EdictTestApp` exposes four — `Timeline`, `GetSagaProgress`, `GetProjectionRow`, and the metrics-cache probes — so a test asserts an outcome without ever calling `Task.Delay`.

## Smallest valid probe

```csharp
using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Domain.Orders.CommandHandlers;
using Sample.Domain.Payments.Sagas;

using Xunit;

public sealed class OrderPaymentSagaTests
{
    [Fact]
    public async Task OrderPaymentSaga_ShouldReachConfirmed_WhenAmountIsBelowDeclineThreshold()
    {
        var orderId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.Send(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Send(new AddLineItemCommand(orderId, Guid.NewGuid(), "SKU-1", 1));
        await app.Send(new SubmitOrderCommand(orderId, Amount: 100m));
        await app.Drain();

        var progress = await app.GetSagaProgress<OrderPaymentSaga, OrderPaymentProgress>(orderId);

        await Verify(progress);
    }
}
```

`Drain` returns when the in-process engine has nothing left to do — the inline outbox drain has run, the in-process publisher has fanned every event out, every cascading `SendCommand` has settled, and the chaos held-queue is empty. No timeout-shaped `Task.Delay` is involved.

## Surface

- **`EdictTestApp.Timeline → Timeline`** — the recorded sequence of every Command sent, Event raised, and consumer Invocation observed. Volatile envelope fields (ids, timestamps, W3C trace context) are scrubbed; the snapshot is the deterministic wire-format drift guard. Assert with `await Verify(app.Timeline)`.
- **`EdictTestApp.GetSagaProgress<TSaga, TProgress>(Guid key) → Task<TProgress>`** — typed read of the saga grain's durable `Progress` for direct snapshot assertion. `TSaga` is the saga implementation class; `TProgress` is its progress type. Routes through `IEdictSaga` plus a class-name prefix because Orleans's codegen runs before Edict's generator and so does not produce a client proxy for the generator-emitted `I{Saga}` interface.
- **`EdictTestApp.GetProjectionRow<TRow>(string tableName, string partitionKey, string rowKey) → Task<TRow?>`** — typed read of the projection row a `EdictTableProjectionBuilder<TRow>` last wrote for the supplied `(tableName, partitionKey, rowKey)`, or `null` when the projection's `Handle` never ran for this key.
- **`EdictTestApp.GetOutboxState(string grainType) → (int TotalPending, DateTimeOffset? OldestEnqueuedAt)`** — the silo-local metrics cache the `edict.outbox.pending.count` and `edict.outbox.oldest_entry.age` observable gauges read at scrape time, summed across every live grain of `grainType` on this silo.
- **`EdictTestApp.GetSagaState(string sagaType) → DateTimeOffset?`** — the most-recent `lastHandledAt` across every live saga of `sagaType` on this silo, or `null` when no saga of that type has handled an event. Pairs with `AdvanceClock` to verify `edict.saga.progress.age` grows when a saga sits idle.
- **`EdictTestApp.Drain() → Task`** — settles the engine. Polls timeline-recorder count for a stability window (250 ms), flushes the chaos held-queue, and re-polls until both have gone quiet. Hard timeout 10 s — exceeded only when the consumer's `Handle` itself does not terminate.
- **`EdictTestApp.AdvanceClock(TimeSpan) → Task`** — advances the `FakeTimeProvider` the engine reads for backoff and reminder gating, then drains. Backoff timing elapses with no wall-clock wait.

## Why not `Task.Delay`

Wall-clock delay is not a useful settlement primitive. A `Task.Delay(500)` either fires before the cascade has finished (the test asserts on a half-settled state and flakes) or fires long after (the test wastes the difference on every run). `Drain` waits exactly as long as the in-process engine takes — no faster, no slower — and `AdvanceClock` moves the engine's gating clock without moving the wall.

The same principle holds inside the harness itself: the chaos held-queue is released on `Drain`, not on a timer. There is no `WithHoldFor(TimeSpan)` knob anywhere on the surface, and there will not be one. If a test is reaching for `Task.Delay`, the answer is to `await app.Drain()` again or to advance the virtual clock.

## What the timeline records

Every `Send` (through the recording `IEdictSender` decorator), every `Raise` (when the in-process publisher dispatches a stamped event), and every consumer `Handle` completion (`Ran`) or dead-letter promotion (`DeadLettered`) lands as a `TimelineEntry`. The entry carries the type's short name, the domain payload, and not much else — `EventId`, `OccurredAt`, `TraceId`, and `SpanId` are deliberately excluded so the Verify snapshot stays stable run-to-run.

Permanent-failure outcomes are recorded out-of-band by the publish executor when it observes the framework's `EdictDeadLetterRaised` event with `Kind = InvokeHandler`, because the host's promotion path bypasses the invoke-handler executor on the final attempt. The timeline still shows the `arrived → DeadLettered` pair.

## Probing one row vs. one whole workflow

The Verify-shaped `Timeline` is the default. A targeted probe (`GetSagaProgress`, `GetProjectionRow`, `GetOutboxState`) is the right fit when:

- A test is sensitive to chaos reordering in a way the timeline is not. The `OrderPaymentSaga` happy-path test asserts saga progress only because the same workflow's `OrdersByStatus` projection-row state is order-sensitive and is independently pinned by the projection's own test.
- A test asserts on metrics observability. `GetOutboxState` and `GetSagaState` read the same cache the observable gauges would scrape, so `Assert.Equal` on these reproduces what a `MeterListener` would see.
- A test asserts a single value (one Guid, one count) where a full Verify snapshot would be noise.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Saga`, `Projection Builder`, `Outbox`.
- Testing — [setup.md](setup.md), [chaos.md](chaos.md), [seams.md](seams.md).
- ADRs — [0024 — Test layering](../../adr/0024-test-layering.md), [0025 — Chaos-default models at-least-once delivery](../../adr/0025-chaos-default-models-at-least-once-delivery.md), [0040 — Silo-local metrics cache for observable gauges](../../adr/0040-silo-local-metrics-cache-for-observable-gauges.md).
