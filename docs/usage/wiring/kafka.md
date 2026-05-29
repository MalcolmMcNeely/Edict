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

## `EdictKafkaStreamsOptions`

| Property | Default | Purpose |
| --- | --- | --- |
| `StreamProviderName` | `"edict"` | Orleans stream-provider name. The runtime is pinned to `"edict"`; do not change. |
| `BootstrapServers` | `""` | **Required.** Kafka `bootstrap.servers` connection string. No default — there is no defensible literal here. Empty value throws `EdictWiringException` at wiring time. |
| `ConsumerGroupId` | `"edict-silo"` | Kafka consumer group id. All silos sharing this id share the partition assignment, which is how Edict scales horizontally — one consumer group per silo deployment. |
| `PartitionCount` | `32` | Default partition count for every Edict-owned Kafka topic. Receivers are one-per-partition; per-aggregate ordering is preserved by the stream-key → partition mapping inside the adapter. |
| `PartitionCountByStream` | empty | Per-stream partition-count overrides keyed by `[Stream]` name. A hot stream can sit on a larger fan-out than the rest of the fleet. Streams not in this map fall back to `PartitionCount`. |
| `ReplicationFactor` | `3` (auto-clamping) | Topic replication factor. The production floor for surviving one broker loss. The provisioner auto-clamps to the available broker count **only when this option is left at its default**; assigning it (even to `3`) opts into strict mode and the provisioner throws if the cluster cannot satisfy the request. |
| `MinInSyncReplicas` | derived | `min.insync.replicas` for every Edict-owned topic. Derived from `ReplicationFactor` as `max(1, RF − 1)`. Read-only. |
| `Compression` | `Lz4` | Compression codec applied to every produced batch. Best wire-size / CPU trade-off for JSON-shaped payloads. |
| `MessageTimeout` | `30 s` | Maximum time a produced message may sit in the producer queue across retries. Maps to librdkafka's `message.timeout.ms`. librdkafka's own default is 5 minutes — far past Orleans' ~30 s grain-call timeout, which would queue grain-call timeouts behind producer retries during a sustained broker outage. Edict's 30 s matches Orleans' shape. |
| `AutoOffsetReset` | `Latest` | Where a fresh consumer-group member starts when no committed offset exists. Edict is event-driven, not event-sourced — a fresh consumer picks up new events from the moment it joins, not from the beginning of the topic. |
| `ProducerConfigOverrides` | empty | Raw `Confluent.Kafka` producer config keys merged into the built `ProducerConfig` — escape hatch for tuning a knob Edict has not yet surfaced. Wiring rejects any entry that would downgrade `acks` from `all` or flip `enable.idempotence` off; the factory re-stamps both floors after merging. |
| `ConsumerConfigOverrides` | empty | Raw `Confluent.Kafka` consumer config keys merged into the built `ConsumerConfig`. Wiring rejects any entry that would flip `enable.auto.commit` back on; the factory re-stamps `enable.auto.commit=false` after merging. |

The producer and consumer contract floors (`acks=all`, `enable.idempotence=true`, `enable.auto.commit=false`, manual commit after `HandleAsync` returns) are non-negotiable and are not exposed.

## Connection strings

Kafka uses a raw `bootstrap.servers` connection string set on `BootstrapServers`. Format is `host1:port1,host2:port2,…`. Local development pulls it from Aspire's `kafka` service binding; production passes the broker list from configuration (typically `appsettings.json` or environment variables). Edict does no client-side authentication wiring — SASL/SSL settings go through `ProducerConfigOverrides` / `ConsumerConfigOverrides`.

## Gotchas

### Per-stream options must be resolved as a singleton, not via `IOptionsMonitor`

`EdictKafkaAdapterFactory` resolves `EdictKafkaStreamsOptions` as a DI singleton instance directly, **not** through `IOptionsMonitor<EdictKafkaStreamsOptions>`. Orleans' named-options path (the shape most stream providers wire through) silently drops the dictionary fields — `PartitionCountByStream`, `ProducerConfigOverrides`, `ConsumerConfigOverrides` — and any other reference-type field set after construction. The mapper would then never see the per-stream overrides, and a hot stream's partition count would silently fall back to the fleet-wide `PartitionCount` without a wiring-time error. The singleton-resolution path is the one safe form; do not refactor it to `IOptionsMonitor` when forking this extension.

### Edict-opinionated defaults vs. Orleans-conservative defaults

Three knobs ship with values above what an Orleans-defaults setup would land at. The headline trade-off is throughput vs. baseline cost — Edict opts for throughput.

| Knob | Edict default | Orleans default | Effect |
| --- | --- | --- | --- |
| `PartitionCount` | `32` | adapter-dependent | Receiver fan-out per Edict-owned topic. 32 is defensible for tens-of-silos / kilo-events-per-sec workloads without controller overhead. |
| `QueuePollingPeriod` (AQS sibling) | `10 ms` | `100 ms` | Per-event latency floor. Mirrored stance: Edict's defaults assume the consumer wants interactive latency out of the box. |
| `NumQueues` (AQS sibling) | `16` | `8` | Stream-provider fan-out. Lifts the consumer-parallelism ceiling against Orleans-conservative defaults. |

A cost-sensitive workload should lower `PartitionCount` (and on AQS, `NumQueues`) and raise `QueuePollingPeriod`. A high-throughput workload should leave the defaults alone and consider `PartitionCountByStream` for the hottest streams.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Domain Stream`, `Event`, `Outbox`.
- Concepts — [events.md](../concepts/events.md), [event-handlers.md](../concepts/event-handlers.md), [projection-builders.md](../concepts/projection-builders.md), [telemetry.md](../concepts/telemetry.md).
- Wiring — [azure-persistence.md](azure-persistence.md), [postgres.md](postgres.md).
- ADRs — [0028 — Custom Kafka stream provider](../../adr/0028-custom-kafka-stream-provider.md), [0023 — Config surface and installation](../../adr/0023-config-surface-and-installation.md).
