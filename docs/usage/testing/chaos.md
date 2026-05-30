# Chaos

`Edict.Testing` applies bounded duplicate redelivery and bounded reorder to every published event on every test run. The contract is on by default with no opt-out — production streams redeliver and reorder, so making both default test conditions catches consumers that quietly rely on exactly-once or strict order.

## Smallest valid test exercising chaos

```csharp
using Edict.Testing;

using Xunit;

public sealed class WidgetCounterTests
{
    [Fact]
    public async Task ReorderFragileProjection_LandsZero_UnderDefaultChaos()
    {
        var widgetId = Guid.Parse("66666666-6666-6666-6666-666666666666");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(WidgetCounterTests).Assembly));

        await app.SendAsync(new PlaceWidgetCommand(widgetId));
        await app.SendAsync(new IncrementWidgetCommand(widgetId));
        await app.Drain();

        var row = await app.GetProjectionRow<WidgetCounterRow>(
            tableName: "widgetcounter",
            partitionKey: widgetId.ToString(),
            rowKey: "counter");

        // Strict order would leave Count = 1. Reorder lands WidgetPlaced after
        // WidgetIncremented, so WidgetPlaced's Count = 0 reset wins last.
        Assert.NotNull(row);
        Assert.Equal(0, row.Count);
    }
}
```

A "reset on Place" projection is order-sensitive on purpose — under default chaos it lands `Count = 0` rather than `Count = 1`. The test passes because reorder is on, not because the test author asked for it. A consumer who reads `Count = 0` and goes back to fix their projection is exactly the win.

## Behaviour

Two behaviours model the at-least-once contract:

- **Duplicate redelivery** — gated by `DuplicateProbability = 0.5` with up to `MaxExtraDeliveries = 1` extra dispatch per emission, so the consumer dedup ring (the `EdictIdempotencyBase` per-grain ring) is exercised in every multi-step test.
- **Bounded reorder** — gated by `ReorderProbability = 0.3` with held-queue depth capped at `MaxReorderDistance = 2`, per-subscriber and per-aggregate, so consumers exercise the reorder-tolerance contract.

A single `Seed = 0xED1C7` seeds two independent RNG streams via XOR constants — `new Random(Seed)` for duplicate, `new Random(Seed ^ 0x5_EE_D5)` for reorder. Tuning one probability does not re-baseline tests gated by the other. Determinism is not configurable; there is no `WithoutChaos`, no `WithChaosSeed`, no environment toggle, no per-test escape hatch.

Reorder release is `Drain`-triggered. On every stability window the harness flushes the held queue through the same dispatch path used on arrival; if release surfaced new arrivals the stability gate resets and the cycle repeats. No wall-clock hold is involved.

## What chaos is not

- **Not a network-fault simulator.** Latency, drops, and partial partitions are out of scope. No Toxiproxy.
- **Not a malformed-data simulator.** Bad payloads land in dead-letter via the runtime contract. Chaos does not produce them.
- **Not a substrate-layer fault simulator.** Broker kill, rebalance, and mid-handler crash are resilience tests using native APIs (Testcontainers `PauseAsync` / `RestartAsync`, `TestCluster.StopSilo`). They live in the framework's provider suites against real Azurite / Postgres / Kafka, not in the consumer-facing harness.
- **Not global or cross-aggregate reorder.** The reorder scope is per-subscriber-per-aggregate, mirroring the framework's reorder-tolerance contract. Modelling broader reorder would assert a stricter contract than the framework offers.
- **Not within-`HandleAsync` raised-event reorder.** Events raised in one `HandleAsync` publish in raise order on the happy path; reordering them under test would make the harness less faithful to production, not more.
- **Not failure injection.** Transient throws and provider timeouts are forensic seams the framework uses to prove its own dead-letter and retry pathways, not consumer surface.

## When to disable

There is no built-in disable. A test that needs strict ordering for a diagnostic reason — bisecting a regression, narrowing a flake to chaos versus consumer code, asserting on an order-stable contract — fixes the cause, not the harness. Two paths exist when ordering must be removed from the experiment:

- **Use `Timeline` instead of a probe** for steps where any order satisfies the workflow. The default Verify snapshot is the broader assertion; targeted probes are the narrower one. A test asserting the saga reaches `Confirmed` does not need to assert the order in which events were emitted.
- **Split the test.** If two assertions in one test pull against each other — one wants to ride chaos, the other wants exact order — separate them. The reorder-sensitive `OrdersByStatus` projection has its own test pinned independently from the `OrderPaymentSaga` happy path, because they sample different parts of the same workflow.

If a flake survives both moves, the consumer has a real reorder-handling bug. That is the chaos contract working as designed — the answer is the consumer code, not a `WithoutChaos` knob.

## The event-handler carve-out

`EdictEventHandler` activations skip duplicate redelivery — consumer mock-call-count assertions on an Event Handler would otherwise be non-deterministic. Reorder still applies to event handlers because call counts are invariant under reorder.

The carve-out is internal to the chaos roller. A consumer does not write `[EdictEventHandler]` or similar — every `EdictEventHandler`-derived grain class gets the carve-out automatically.

## What chaos does to a Verify snapshot

Reordered arrivals show in the timeline as recorded; the snapshot is not normalised back to raise order. The `EventId`, `OccurredAt`, and trace fields are scrubbed (see [probes.md](probes.md)), so the snapshot stays stable across runs even though wall-clock timing varies. If a snapshot ordering drifts on rerun, the chaos seed has the same value but a consumer-side state mutation is run-order-dependent — that is the bug.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Idempotency Base`, `Outbox`, `Event`.
- Testing — [setup.md](setup.md), [probes.md](probes.md), [seams.md](seams.md).
- Concepts — [idempotency.md](../concepts/idempotency.md), [dead-letter.md](../concepts/dead-letter.md).
- ADRs — [0002 — Idempotency model](../../adr/0002-idempotency-model.md), [0015 — Outbox engine](../../adr/0015-outbox-engine.md), [0025 — Chaos-default models at-least-once delivery](../../adr/0025-chaos-default-models-at-least-once-delivery.md).
