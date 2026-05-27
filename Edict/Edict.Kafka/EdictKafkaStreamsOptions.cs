namespace Edict.Kafka;

/// <summary>
/// Tuning knobs for the Edict Kafka stream provider. Tracer-bullet surface
/// (ADR-0028 draft) — only the properties needed to bring a silo up against a
/// fresh broker. Producer floors (<c>acks=all</c>, idempotent producer on,
/// lz4 compression) and consumer floors (<c>enable.auto.commit=false</c>,
/// manual commit after <c>HandleAsync</c> returns, <c>AutoOffsetReset.Latest</c>)
/// are hardcoded inside the adapter for now; the full surface lands in #139b.
/// </summary>
public sealed class EdictKafkaStreamsOptions
{
    /// <summary>Orleans stream-provider name. Edict's runtime is hardcoded to <c>"edict"</c>.</summary>
    public string StreamProviderName { get; set; } = "edict";

    /// <summary>
    /// Kafka <c>bootstrap.servers</c> connection string. Required — no default,
    /// because there is no defensible default broker address.
    /// </summary>
    public string BootstrapServers { get; set; } = "";

    /// <summary>
    /// Kafka consumer group id. All silos sharing this id share the partition
    /// assignment, which is how Edict scales horizontally — one consumer group
    /// per silo-deployment.
    /// </summary>
    public string ConsumerGroupId { get; set; } = "edict-silo";

    /// <summary>
    /// Partition count for every Edict-owned Kafka topic. Receivers are
    /// one-per-partition; per-aggregate ordering is preserved by the stream
    /// key → partition mapping inside the adapter. Default 32 — defensible for
    /// tens-of-silos / kilo-events-per-sec workloads without controller
    /// overhead.
    /// </summary>
    public int PartitionCount { get; set; } = 32;
}
