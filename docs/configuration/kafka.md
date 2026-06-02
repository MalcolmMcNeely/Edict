# Kafka configuration

`EdictKafkaStreamsOptions` backs `AddEdictKafkaStreams`, the single extension that wires Edict's custom Kafka `IQueueAdapter`, the topic provisioner, and the streams marker the wiring validator inspects. For the `Add*` call shape, the client-side setup, and the framework-author gotchas (notably the singleton-resolution trap), see [wiring/kafka.md](../usage/wiring/kafka.md).

## `EdictKafkaStreamsOptions`

| Property | Default | Purpose |
| --- | --- | --- |
| `StreamProviderName` | `"edict"` | Orleans stream-provider name. The runtime is pinned to `"edict"`; do not change. |
| `BootstrapServers` | `""` | **Required.** Kafka `bootstrap.servers` connection string. No default — there is no defensible literal here. An empty value throws `EdictWiringException` at wiring time. |
| `ConsumerGroupId` | `"edict-silo"` | Kafka consumer group id. All silos sharing this id share the partition assignment, which is how Edict scales horizontally — one consumer group per silo deployment. |
| `PartitionCount` | `32` | Default partition count for every Edict-owned Kafka topic. Receivers are one-per-partition; per-aggregate ordering is preserved by the stream-key → partition mapping inside the adapter. See the cost trade-off below. |
| `PartitionCountByStream` | empty | Per-stream partition-count overrides keyed by `[Stream]` name. A hot stream can sit on a larger fan-out than the rest of the fleet. Streams not in this map fall back to `PartitionCount`. Resolve the effective count for a stream with the `PartitionCountFor(streamName)` helper. |
| `ReplicationFactor` | `3` (auto-clamping) | Topic replication factor — the production floor for surviving one broker loss. The provisioner auto-clamps to the available broker count **only when this option is left at its default**; assigning it (even to `3`) opts into strict mode and the provisioner throws if the cluster cannot satisfy the request. |
| `MinInSyncReplicas` | derived | `min.insync.replicas` for every Edict-owned topic. Derived from `ReplicationFactor` as `max(1, RF − 1)`. Read-only. |
| `Compression` | `Lz4` | Compression codec applied to every produced batch. Best wire-size / CPU trade-off for JSON-shaped payloads. |
| `MessageTimeout` | `30 s` | Maximum time a produced message may sit in the producer queue across retries. Maps to librdkafka's `message.timeout.ms`. librdkafka's own default is 5 minutes — far past Orleans' ~30 s grain-call timeout, which would queue grain-call timeouts behind producer retries during a sustained broker outage. Edict's 30 s matches Orleans' shape. |
| `AutoOffsetReset` | `Latest` | Where a fresh consumer-group member starts when no committed offset exists. Edict is event-driven, not event-sourced — a fresh consumer picks up new events from the moment it joins, not from the beginning of the topic. |
| `ProducerConfigOverrides` | empty | Raw `Confluent.Kafka` producer config keys merged into the built `ProducerConfig` — escape hatch for tuning a knob Edict has not yet surfaced. Wiring rejects any entry that would downgrade `acks` from `all` or flip `enable.idempotence` off; the factory re-stamps both floors after merging. |
| `ConsumerConfigOverrides` | empty | Raw `Confluent.Kafka` consumer config keys merged into the built `ConsumerConfig`. Wiring rejects any entry that would flip `enable.auto.commit` back on; the factory re-stamps `enable.auto.commit=false` after merging. |

The producer and consumer contract floors (`acks=all`, `enable.idempotence=true`, `enable.auto.commit=false`, manual commit after `HandleAsync` returns) are non-negotiable and are not exposed.

## Cost vs. throughput trade-off

`PartitionCount` ships at `32`, above what an Orleans-defaults setup would land at. The headline trade-off is throughput vs. baseline cost — Edict opts for throughput.

| Knob | Edict default | Effect |
| --- | --- | --- |
| `PartitionCount` | `32` | Receiver fan-out per Edict-owned topic. 32 is defensible for tens-of-silos / kilo-events-per-sec workloads without controller overhead. More partitions lift the parallelism ceiling at the cost of more open file handles and controller metadata per broker. |

A cost-sensitive workload should lower `PartitionCount` — fewer partitions mean fewer receivers and less broker bookkeeping, trading the parallelism ceiling for a smaller footprint. A high-throughput workload should leave the default alone and reach for `PartitionCountByStream` to give only the hottest streams a larger fan-out, rather than raising the fleet-wide default.

## Connection strings

Kafka uses a raw `bootstrap.servers` connection string set on `BootstrapServers`. Format is `host1:port1,host2:port2,…`. Local development pulls it from Aspire's `kafka` service binding; production passes the broker list from configuration (typically `appsettings.json` or environment variables). Edict does no client-side authentication wiring — SASL/SSL settings go through `ProducerConfigOverrides` / `ConsumerConfigOverrides`.

## See also

- [index.md](index.md) — the installation surface and fail-fast validation behaviour.
- [wiring/kafka.md](../usage/wiring/kafka.md) — the `Add*` call shape, client setup, and the `IOptionsMonitor` singleton-resolution gotcha.
- [core.md](core.md) — the provider-agnostic `AddEdict()` knobs.
- ADRs — [0028 — Custom Kafka stream provider](../adr/0028-custom-kafka-stream-provider.md), [0023 — Config surface and installation](../adr/0023-config-surface-and-installation.md).
