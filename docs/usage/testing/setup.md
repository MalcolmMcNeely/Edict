# Test setup

`Edict.Testing` ships an in-memory test framework that boots the consumer's grains on a single-silo Orleans `TestCluster`, auto-wires Edict the same way production does, and runs the real Outbox / saga / projection engine over memory streams and an in-memory single store. A whole workflow is asserted with one `await Verify(app.Timeline)`.

## Smallest valid test

```csharp
using Edict.Testing;

using Sample.Contracts.Orders.Commands;
using Sample.Domain.Orders.CommandHandlers;

using Xunit;

public sealed class PlaceOrderTests
{
    [Fact]
    public async Task PlaceOrder_ShouldLandOnTimeline()
    {
        var orderId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(OrderCommandHandler).Assembly));

        await app.SendAsync(new PlaceOrderCommand(orderId, "REF-001"));
        await app.Drain();

        await Verify(app.Timeline);
    }
}
```

The consumer assembly is the only required input. `EdictTestApp` reflects over it to build the implicit-subscription map and call the generated `AddEdict()` registration.

## Surface

- **`EdictTestApp.StartAsync(Action<EdictTestAppBuilder>) → Task<EdictTestApp>`** — boots the in-memory cluster. Returns an `IAsyncDisposable`; always `await using`.
- **`EdictTestAppBuilder.WithConsumer(Assembly)`** — the consumer assembly whose grains, commands, events and generated `AddEdict()` the cluster boots. Required.
- **`EdictTestAppBuilder.Replace<TService>(TService fake)`** — last-`AddSingleton`-wins override applied to both the silo and client containers. See [seams.md](seams.md).
- **`EdictTestApp.SendAsync(EdictCommand) → Task<EdictCommandResult>`** — dispatches through the real `IEdictSender` on the cluster's client.
- **`EdictTestApp.Drain() → Task`** — settles the in-process engine on a stable timeline. Probes go through here, not `Task.Delay`. See [probes.md](probes.md).
- **`EdictTestApp.AdvanceClock(TimeSpan) → Task`** — advances the virtual `TimeProvider` the engine reads for backoff and reminder gating, then drains.
- **`EdictTestApp.Timeline`** — the single Verify-shaped snapshot of every Command sent, Event raised, and consumer Invocation observed.

## What the harness wires for you

A test never writes `TestClusterBuilder` itself. `StartAsync` runs an `ISiloConfigurator` and an `IClientBuilderConfigurator` that register the same shapes a production silo and client do, against the consumer assembly:

- **Serializer (silo and client)** — `services.AddSerializer(s => s.AddAssembly(consumerAssembly).AddAssembly(typeof(IEdictCommandHandler).Assembly).AddEdictContractSerializer())`. The consumer assembly contributes its generated Orleans `TypeManifest`; `Edict.Core`'s `IEdictCommandHandler` assembly contributes the framework's; `AddEdictContractSerializer` routes contract types through MessagePack with string keys.
- **Edict registration (silo and client)** — `AddEdict()` once per container. The silo additionally calls `AddEdictOutbox()` to install the Outbox drain engine.
- **Grain storage** — `AddMemoryGrainStorage("PubSubStore")` plus `AddMemoryGrainStorage("edict-state")` for both the unified grain-state envelope (`Outbox` + `Idempotency` + per-grain `Payload`) and the test `PubSubStore`.
- **Reminders** — `UseInMemoryReminderService`.
- **Time** — `Microsoft.Extensions.Time.Testing.FakeTimeProvider` registered as the `TimeProvider`, starting at `2026-01-01T00:00:00Z`. `AdvanceClock` is the consumer-facing knob.
- **Outbox / saga / event-handler executors** — the same `OutboxHost`, `EdictSaga`, and `EdictDeadLetterProjectionBuilder` the production silo runs. The bare `PublishEventExecutor` and `InvokeHandlerExecutor` are swapped for in-process equivalents — see [seams.md](seams.md).

## Silo / client provider split

The serializer is registered in **both** containers because Orleans's grain-call codec runs on each side of the wire. The client process needs the consumer's grain-interface assembly so its outgoing calls (`IEdictSender.SendAsync`) can serialise concrete commands; the silo needs the consumer's handler assembly so its incoming activations can deserialise them.

`AddEdict()` is also registered on both sides for the same reason — the `IEdictSender` lives on the client; the Outbox engine, sagas, projections, and dead-letter pipeline live on the silo.

Memory streams are registered on the silo only — `AddMemoryStreams("edict")`. The in-process publisher bypasses the stream provider entirely (the Orleans memory-stream pulling agent does not deliver to referenced-assembly consumers, which is why the bypass exists), but `EdictIdempotencyBase`'s `OutboxHost` still resolves a stream provider by name at construction, so the registration is kept to satisfy that resolve.

## Where this lives

`Edict.Testing` is the **consumer** test framework. It is referenced by consumer test projects only — the Sample app's `Sample.Azure.Silo.Tests`, `Sample.KafkaPostgres.Silo.Tests`, and similar. Internal framework test projects do not reference it; the real-transport test batteries against Azurite, Postgres, and Kafka via Testcontainers prove the framework directly. The split is the test-layering rule:

- **`Edict.Core.Tests`** — pure-logic unit tests (`OutboxSlice` transitions, route discovery, options validators, MessagePack wire-shape Verify guards). No Testcontainers, no Azurite. Reaching for an external container in this project is a smell.
- **`Edict.Azure.Tests` / `Edict.Postgres.Tests` / `Edict.Kafka.Tests`** — the mechanism battery against real infra via Testcontainers. The at-least-once and dedup proofs, resilience scenarios, and load proofs live here.
- **`Edict.Testing` (this surface)** — the in-memory harness consumers use to test their own code. The Sample app's tests dogfood it.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Command`, `Event`, `Sender`, `Outbox`, `Idempotency Base`.
- Testing — [probes.md](probes.md), [chaos.md](chaos.md), [seams.md](seams.md).
- ADRs — [0007 — Edict.Contracts boundary](../../adr/0007-edict-contracts-boundary.md), [0024 — Test layering](../../adr/0024-test-layering.md), [0025 — Chaos-default models at-least-once delivery](../../adr/0025-chaos-default-models-at-least-once-delivery.md).
