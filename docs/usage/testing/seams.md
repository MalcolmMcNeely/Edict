# Seams

Two seams keep consumer-side test code substitutable while the framework's own grains stay real: `EdictTestAppBuilder.Replace<TService>` for consumer-owned collaborators a handler depends on, and the internal `IEdictEventConsumer` unified delivery interface that the in-process publisher uses in place of the Orleans memory-stream pulling agent. Only the first is consumer surface; the second is documented so a consumer reading the Edict source tree is not surprised by what they see.

## Replace a consumer collaborator

```csharp
using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Domain.Orders;
using Sample.Domain.Orders.CommandHandlers;

using Xunit;

public sealed class OrderEmailHandlerReplaceTests
{
    [Fact]
    public async Task Replace_ShouldRouteEmailNotifierCalls_ToTheFake()
    {
        var orderId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var fake = new RecordingEmailNotifier();

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly)
            .Replace<IEmailNotifier>(fake));

        await app.SendAsync(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Drain();

        Assert.Single(fake.SentOrderIds);
        Assert.Equal(orderId, fake.SentOrderIds[0]);
    }

    sealed class RecordingEmailNotifier : IEmailNotifier
    {
        public List<Guid> SentOrderIds { get; } = new();

        public Task SendOrderPlacedAsync(Guid orderId, Guid eventId)
        {
            SentOrderIds.Add(orderId);
            return Task.CompletedTask;
        }
    }
}
```

The `IEmailNotifier` the production silo wires gets overridden on **both** the silo and client containers. The fake intercepts the `OrderEmailEventHandler`'s deferred invocation; the rest of the workflow (the command, the event publish, the dedup ring) runs against the real Edict engine.

## Surface

- **`EdictTestAppBuilder.Replace<TService>(TService fake)`** — registers `fake` as the resolved implementation of `TService` on both the silo and client containers. Returns the builder for chaining.
- **`IEdictSender`** (`Edict.Contracts.Sending`) — already decorated by the harness. `SendAsync` is recorded onto the timeline before delegating to the real sender; a consumer rarely replaces it explicitly. The `Edict.Testing` swap of this seam is the in-memory implementation the production `IEdictSender` is exchanged for.

## How `Replace` resolves

The Microsoft DI container resolves the **last** `AddSingleton(TService, instance)` registration for a given service type. `Replace<TService>(fake)` is applied **after** every framework and consumer registration on both containers:

1. `AddEdict()` and `AddEdictOutbox()` register their defaults.
2. The harness adds its in-process executor swaps and the recording sender decorator.
3. Each `Replace<TService>(fake)` runs as a final `services.AddSingleton(typeof(TService), fake)`.

Last-in-wins, so the fake takes precedence. The fake is shared between silo and client — a single instance is captured by closure on both configurators, so a consumer asserting on the fake from the test thread sees writes the silo-side handler performed.

## What can and cannot be replaced

- **Replaceable** — any DI-registered collaborator the consumer's code resolves: notifiers, clock-adjacent helpers the consumer owns, an outbound HTTP client wrapper, a tenant lookup. Anything the consumer's handler injects through its constructor.
- **Not replaceable** — Orleans grain implementations. Grains are framework-owned and instantiated by Orleans's grain activator, not by Microsoft DI. A test does not stub out a Saga, a Projection Builder, or a Command Handler — those are the code under test. Stubbing them defeats the harness.
- **Already replaced by the harness** — `TimeProvider` (a `FakeTimeProvider` starting at `2026-01-01T00:00:00Z`, advanced via `AdvanceClock`), `IEdictClaimCheckStore` (in-memory dictionary), `IEdictTableStoreFactory` (in-memory factory backing `GetProjectionRow`), the `PublishEvent` and `InvokeHandler` outbox executors (in-process equivalents), and `IEdictSender` (decorated for timeline recording). A `Replace<TService>(fake)` of one of these is allowed but rarely useful and not recommended.

## The `IEdictEventConsumer` unified delivery seam

`IEdictEventConsumer` is the framework-internal grain interface every event-consuming grain shares — every Event Handler, every Saga, every Projection Builder. One method:

```csharp
namespace Edict.Core.Idempotency;

public interface IEdictEventConsumer : IGrainWithGuidKey
{
    [AlwaysInterleave]
    Task OnEdictEventAsync(EdictEvent edictEvent);
}
```

The harness's in-process publisher dispatches a published event by resolving every implicit-subscriber grain class for the event's stream and calling `OnEdictEventAsync` on each, **fire-and-forget per subscriber**. A real stream hop is asynchronous to the publishing grain, so a saga reaction that fans back to the same aggregate must not deadlock on grain-turn reentrancy. `[AlwaysInterleave]` mirrors Orleans's own stream extension behaviour.

The bypass exists because the Orleans memory-stream pulling agent does not deliver to referenced-assembly consumers — every saga and projection lives in the consumer's grain assembly, which the pulling agent does not scan. The in-process publisher reflects over the consumer assembly's `[ImplicitStreamSubscription]` attributes at start-up (the `SubscriberMap`) and dispatches synchronously through `IEdictEventConsumer`. From the Outbox engine's point of view the effect is unchanged — same `OutboxEffectKind.PublishEvent`, same dedup ring, same dead-letter promotion path.

A consumer does not write `IEdictEventConsumer` directly. The framework's grain bases (`EdictCommandHandler<T>`, `EdictSaga<T>`, `EdictTableProjectionBuilder<T>`, `EdictEventHandler`) inherit it. The interface appears in the Edict source tree as `public` because Orleans's codegen needs it; consumers do not implement, decorate, or call it.

## What gets bypassed under test, and what does not

| Mechanism | Under `Edict.Testing` |
| --- | --- |
| `EdictIdempotencyBase` dedup ring | **Real.** Every duplicate dispatched by chaos is suppressed by the consumer's grain exactly as in production. |
| Outbox drain engine | **Real.** Same `OutboxHost`, same `OutboxSlice` transitions, same dead-letter promotion. |
| `EdictDeadLetterProjectionBuilder` | **Real.** Dead-letter rows land in the in-memory table store and can be read via `GetProjectionRow`. |
| Grain persistence | **In-memory.** `AddMemoryGrainStorage("edict-state")` rather than Azure / Postgres. |
| Stream hop | **Bypassed.** The in-process publisher dispatches synchronously through `IEdictEventConsumer`; the memory-stream pulling agent is not exercised. |
| Wire serialisation | **Real.** Events are serialised and deserialised through the same MessagePack contract pipeline production uses. The wire-shape Verify drift guard catches changes here. |
| Trace / W3C continuity | **Real.** Spans open per publish and per invocation, with the trace parent restored from the event's `TraceId` / `SpanId` (events stamp these at raise). |
| Claim check | **In-memory dictionary.** Same threshold, same envelope shape. |
| `TimeProvider` | **Virtual.** `FakeTimeProvider` advanced by `AdvanceClock`. |

The bypassed surface is the stream hop and the storage backing — everything else is the real framework code. Tests that need the real Orleans memory-stream pulling agent or a real Azurite queue are framework-internal and live in `Edict.Azure.Tests` against Testcontainers; they do not use `Edict.Testing`.

## Why this surface is consumer-only

`Edict.Testing` is the **shipped** harness for consumers of Edict. Internal framework test projects (`Edict.Core.Tests`, `Edict.Azure.Tests`, `Edict.Postgres.Tests`, `Edict.Kafka.Tests`, `Edict.Telemetry.Tests`, `Edict.Architecture.Tests`) do not reference `Edict.Testing` and never will. The harness is dogfooded via the Sample app's test projects — those tests use it the way an external consumer does.

A consumer reading the Edict source tree will see `Edict.Testing` referenced by `Edict.Testing.Tests` (proving the harness itself) and by `Sample.Azure.Silo.Tests` / `Sample.KafkaPostgres.Silo.Tests` (dogfooding it), and by nothing else. That is by design — conflating the consumer harness with the framework's own real-transport batteries would erode the integration-test discipline that makes the framework trustworthy.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Sender`, `Idempotency Base`, `Outbox`, `Saga`, `Projection Builder`.
- Testing — [setup.md](setup.md), [probes.md](probes.md), [chaos.md](chaos.md).
- Concepts — [event-handlers.md](../concepts/event-handlers.md), [sagas.md](../concepts/sagas.md), [projection-builders.md](../concepts/projection-builders.md).
- ADRs — [0017 — Hand-authored AddEdict](../../adr/0017-hand-authored-addedict.md), [0024 — Test layering](../../adr/0024-test-layering.md), [0025 — Chaos-default models at-least-once delivery](../../adr/0025-chaos-default-models-at-least-once-delivery.md).
