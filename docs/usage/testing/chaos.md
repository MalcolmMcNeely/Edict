# Chaos

`Edict.Testing` applies bounded duplicate redelivery and bounded reorder to every published event on every test run. The contract is on by default — production streams redeliver and reorder, so making both default test conditions catches consumers that quietly rely on exactly-once or strict order. The one carve-out is `WithoutChaos()`, a narrow opt-out for timeline characterization snapshots (see [When to disable](#when-to-disable)).

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

A single process-wide seed drives two independent RNG streams via XOR constants — `new Random(Seed)` for duplicate, `new Random(Seed ^ 0x5_EE_D5)` for reorder. Tuning one probability does not re-baseline tests gated by the other.

The seed is **random per run**, not a fixed constant. It is taken from the `EDICT_CHAOS_SEED` environment variable when that is set (both `0x`-prefixed hex and decimal parse), otherwise one random `int` is generated once at process start and shared by every `EdictTestApp` in the run — so per-test determinism holds (each app builds its own `Random` instances from the same value) while the run as a whole explores a fresh interleaving each time. A fixed seed proves exactly one path forever; varying it is where the reorder-tolerance coverage actually accrues.

The seed is **surfaced on every run**: written to standard output once and exposed as `EdictChaos.CurrentSeed`, so a failing run's log always carries the value that produced it. Reproduction is *override-to-reproduce* — copy the printed seed into `EDICT_CHAOS_SEED` to pin the whole run to that interleaving and make the failure reappear deterministically:

```
EDICT_CHAOS_SEED=123456789 dotnet test
```

There is deliberately no per-test seed knob: a per-test override would invite reseeding *away* from a failure, the opposite of what reproduction needs.

Reorder release is `Drain`-triggered. On every stability window the harness flushes the held queue through the same dispatch path used on arrival; if release surfaced new arrivals the stability gate resets and the cycle repeats. No wall-clock hold is involved.

## What chaos is not

- **Not a network-fault simulator.** Latency, drops, and partial partitions are out of scope. No Toxiproxy.
- **Not a malformed-data simulator.** Bad payloads land in dead-letter via the runtime contract. Chaos does not produce them.
- **Not a substrate-layer fault simulator.** Broker kill, rebalance, and mid-handler crash are resilience tests using native APIs (Testcontainers `PauseAsync` / `RestartAsync`, `TestCluster.StopSilo`). They live in the framework's provider suites against real Azurite / Postgres / Kafka, not in the consumer-facing harness.
- **Not global or cross-aggregate reorder.** The reorder scope is per-subscriber-per-aggregate, mirroring the framework's reorder-tolerance contract. Modelling broader reorder would assert a stricter contract than the framework offers.
- **Not within-`HandleAsync` raised-event reorder.** Events raised in one `HandleAsync` publish in raise order on the happy path; reordering them under test would make the harness less faithful to production, not more.
- **Not failure injection.** Transient throws and provider timeouts are forensic seams the framework uses to prove its own dead-letter and retry pathways, not consumer surface.

## When to disable

The default is chaos on, and an invariant-asserting test should keep it on: if such a test needs strict ordering, it is asserting a stricter contract than Edict guarantees in production, and the answer is the consumer code, not the harness. Two moves remove ordering from the experiment without silencing chaos:

- **Use `Timeline` instead of a probe** for steps where any order satisfies the workflow. The default Verify snapshot is the broader assertion; targeted probes are the narrower one. A test asserting the saga reaches `Confirmed` does not need to assert the order in which events were emitted.
- **Split the test.** If two assertions in one test pull against each other — one wants to ride chaos, the other wants exact order — separate them. The reorder-sensitive `OrdersByStatus` projection has its own test pinned independently from the `OrderPaymentSaga` happy path, because they sample different parts of the same workflow.

The one built-in opt-out is `WithoutChaos()` on the builder, and it is **for timeline characterization snapshots only** — a `Verify(app.Timeline)` whose job is to document the canonical workflow shape, where duplicate/reorder noise is unhelpful and the varying seed would drift the snapshot every run. It zeroes the four chaos knobs for that one app and wins over any seed (`EDICT_CHAOS_SEED` or the random default):

```csharp
await using var app = await EdictTestApp.StartAsync(b => b
    .WithConsumer(typeof(OrderCommandHandler).Assembly)
    .WithoutChaos());
```

`WithoutChaos()` is narrow on purpose. Reach for it when you are *characterizing* the workflow shape, never to quiet an invariant test that should be riding reorder. If an invariant test flakes under chaos, the consumer has a real reorder-handling bug — that is the chaos contract working as designed, and the fix is the consumer code.

## The event-handler carve-out

`EdictEventHandler` activations skip duplicate redelivery — consumer mock-call-count assertions on an Event Handler would otherwise be non-deterministic. Reorder still applies to event handlers because call counts are invariant under reorder.

The carve-out is internal to the chaos roller. A consumer does not write `[EdictEventHandler]` or similar — every `EdictEventHandler`-derived grain class gets the carve-out automatically.

## What chaos does to a Verify snapshot

Reordered arrivals show in the timeline as recorded; the snapshot is not normalised back to raise order. The `EventId`, `OccurredAt`, and trace fields are scrubbed (see [probes.md](probes.md)), so volatile envelope fields never drift the snapshot.

Ordering, however, now *does* drift on rerun for a chaos-on snapshot: because the seed is random per run, two runs explore two interleavings and a `Verify(app.Timeline)` that records reorder as-arrived will diff. That drift is **expected** under a varying seed, not a bug. A timeline snapshot you want stable is a *characterization* snapshot, and it opts out with `WithoutChaos()` (see [When to disable](#when-to-disable)); with chaos off the timeline is exactly raise order on every run and the snapshot is stable. Invariant assertions — counts, final state, set-derived totals — stay stable under the varying seed by construction and need no opt-out.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Idempotency Base`, `Outbox`, `Event`.
- Testing — [setup.md](setup.md), [probes.md](probes.md), [seams.md](seams.md).
- Concepts — [idempotency.md](../concepts/idempotency.md), [dead-letter.md](../concepts/dead-letter.md).
- ADRs — [0002 — Idempotency model](../../adr/0002-idempotency-model.md), [0015 — Outbox engine](../../adr/0015-outbox-engine.md), [0025 — Chaos-default models at-least-once delivery](../../adr/0025-chaos-default-models-at-least-once-delivery.md), [0066 — Chaos seed random-per-run and surfaced](../../adr/0066-chaos-seed-random-per-run-surfaced.md).
