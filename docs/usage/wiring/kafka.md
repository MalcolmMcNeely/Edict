# Kafka wiring

The Kafka streaming side ships in `Edict.Kafka` and is wired through one `ISiloBuilder` extension, `AddEdictKafkaStreams`. It registers Edict's custom `IQueueAdapter` (not Orleans' shipped Kafka providers), the topic provisioner, and the streams marker the wiring validator inspects. Kafka has no shipped persistence — pair this extension with `AddEdictAzurePersistence` or `AddEdictPostgresPersistence`.

## Silo setup

```csharp
using Confluent.Kafka;

using Edict.Core;
using Edict.Core.Serialization;
using Edict.Kafka;

using Orleans.Serialization;

Host.CreateDefaultBuilder(args)
    .UseOrleans((context, silo) =>
    {
        silo.UseLocalhostClustering();
        silo.Services.AddSerializer(ser =>
        {
            ser.AddAssembly(typeof(OrderCommandHandler).Assembly);
            ser.AddEdictContractSerializer();
        });

        silo.AddEdict();

        silo.AddEdictKafkaStreams(o =>
        {
            o.BootstrapServers = context.Configuration.GetConnectionString("kafka")
                ?? throw new InvalidOperationException("Kafka connection string 'kafka' missing.");
            o.ConsumerGroupId  = "my-service-silo";
        });

        // Pair with one of: AddEdictAzurePersistence | AddEdictPostgresPersistence.
    });
```

## Client setup

The client process does not call `AddEdictKafkaStreams` — stream wiring is silo-only. The client registers the consumer's command-handler interface assembly so grain calls can serialise.

```csharp
using Edict.Core;
using Edict.Core.Serialization;

using Orleans.Serialization;

builder.UseOrleansClient(client =>
{
    client.UseLocalhostClustering();
    client.Services.AddSerializer(ser =>
    {
        ser.AddAssembly(typeof(IOrderCommandHandler).Assembly);
        ser.AddEdictContractSerializer();
    });
});

builder.Services.AddEdict();
```

## Configuration

`EdictKafkaStreamsOptions` (the full knob table, including the non-negotiable producer/consumer contract floors that are not exposed), the connection-string format, and the cost-vs-throughput tuning guidance are documented in [configuration/kafka.md](../../configuration/kafka.md).

## Gotchas

### Per-stream options must be resolved as a singleton, not via `IOptionsMonitor`

`EdictKafkaAdapterFactory` resolves `EdictKafkaStreamsOptions` as a DI singleton instance directly, **not** through `IOptionsMonitor<EdictKafkaStreamsOptions>`. Orleans' named-options path (the shape most stream providers wire through) silently drops the dictionary fields — `PartitionCountByStream`, `ProducerConfigOverrides`, `ConsumerConfigOverrides` — and any other reference-type field set after construction. The mapper would then never see the per-stream overrides, and a hot stream's partition count would silently fall back to the fleet-wide `PartitionCount` without a wiring-time error. The singleton-resolution path is the one safe form; do not refactor it to `IOptionsMonitor` when forking this extension.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Domain Stream`, `Event`, `Outbox`.
- Configuration — [kafka.md](../../configuration/kafka.md) — the options table, tuning guidance, and connection-string format.
- Concepts — [events.md](../concepts/events.md), [event-handlers.md](../concepts/event-handlers.md), [projections.md](../concepts/projections.md), [telemetry.md](../concepts/telemetry.md).
- Wiring — [azure-persistence.md](azure-persistence.md), [postgres.md](postgres.md).
- ADRs — [0028 — Custom Kafka stream provider](../../adr/0028-custom-kafka-stream-provider.md), [0023 — Config surface and installation](../../adr/0023-config-surface-and-installation.md).
