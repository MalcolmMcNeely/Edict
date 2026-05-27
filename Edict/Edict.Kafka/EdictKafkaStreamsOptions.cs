using Confluent.Kafka;

namespace Edict.Kafka;

/// <summary>
/// Tuning knobs for the Edict Kafka stream provider. Producer floors
/// (<c>acks=all</c>, idempotent producer on) and consumer floors
/// (<c>enable.auto.commit=false</c>, manual commit after <c>HandleAsync</c>
/// returns) are non-negotiable contract floors that <c>AddEdictKafkaStreams</c>
/// rejects weaker values for — they are not exposed here. Knobs below carry
/// defensible defaults; the raw <c>Confluent.Kafka</c> passthrough hatches
/// (<see cref="ProducerConfigOverrides"/>, <see cref="ConsumerConfigOverrides"/>)
/// cover everything else, with wiring-time refusal of any key that downgrades
/// a floor.
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
    /// Default partition count for every Edict-owned Kafka topic. Receivers
    /// are one-per-partition; per-aggregate ordering is preserved by the
    /// stream key → partition mapping inside the adapter. Default 32 —
    /// defensible for tens-of-silos / kilo-events-per-sec workloads without
    /// controller overhead. Override on a per-stream basis via
    /// <see cref="PartitionCountByStream"/>.
    /// </summary>
    public int PartitionCount { get; set; } = 32;

    /// <summary>
    /// Per-stream partition-count overrides keyed by domain-stream name (the
    /// <c>[Stream]</c> name). A hot stream (high event rate, many silos
    /// consuming) can sit on a larger partition fan-out than the rest of the
    /// fleet without forcing every topic onto the same number. Streams not
    /// present in this map fall back to <see cref="PartitionCount"/>.
    /// </summary>
    public IDictionary<string, int> PartitionCountByStream { get; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// Resolves the partition count for the given domain-stream name —
    /// returns the entry in <see cref="PartitionCountByStream"/> if present,
    /// otherwise falls back to the fleet-wide <see cref="PartitionCount"/>.
    /// </summary>
    public int PartitionCountFor(string streamName) =>
        PartitionCountByStream.TryGetValue(streamName, out var count)
            ? count
            : PartitionCount;

    /// <summary>
    /// Topic replication factor for every Edict-owned Kafka topic. Default 3 —
    /// the production floor for surviving one broker loss without data loss.
    /// The provisioner auto-clamps to the available broker count when this
    /// option is left at its default; setting it explicitly disables the clamp
    /// and the provisioner throws if the cluster cannot satisfy the request.
    /// </summary>
    public short ReplicationFactor { get; set; } = 3;

    /// <summary>
    /// <c>min.insync.replicas</c> applied to every Edict-owned Kafka topic.
    /// Derived from <see cref="ReplicationFactor"/>: <c>RF - 1</c> with a
    /// floor of 1, so rf=3 yields 2 (the production durability stance — one
    /// replica may lag without producer-side <c>acks=all</c> stalls) and rf=1
    /// yields 1 (a single-broker dev cluster cannot satisfy more).
    /// </summary>
    public short MinInSyncReplicas => (short)Math.Max(1, ReplicationFactor - 1);

    /// <summary>
    /// Compression codec applied to every produced batch. Default
    /// <see cref="CompressionType.Lz4"/> — best wire-size / CPU trade-off for
    /// JSON-shaped payloads on modern hardware. Brokers must accept the codec
    /// (any modern broker does).
    /// </summary>
    public CompressionType Compression { get; set; } = CompressionType.Lz4;

    /// <summary>
    /// Where a new consumer-group member starts when no committed offset
    /// exists for its assigned partition. Default
    /// <see cref="AutoOffsetReset.Latest"/> — Edict is event-driven, not
    /// event-sourced (ADR-0001); a fresh consumer should pick up new events
    /// from the moment it joins, not replay the topic from the beginning.
    /// </summary>
    public AutoOffsetReset AutoOffsetReset { get; set; } = AutoOffsetReset.Latest;

    /// <summary>
    /// Raw <c>Confluent.Kafka</c> producer config keys merged into the
    /// <see cref="ProducerConfig"/> the adapter builds — escape hatch for
    /// tuning a knob Edict has not yet surfaced. <c>AddEdictKafkaStreams</c>
    /// validates these at wiring time and throws
    /// <see cref="InvalidOperationException"/> if an entry would downgrade
    /// <c>acks</c> (must remain <c>all</c>) or <c>enable.idempotence</c>
    /// (must remain <c>true</c>). The factory also re-stamps these two floors
    /// after merging, so a missed validation cannot weaken the broker
    /// contract at runtime.
    /// </summary>
    public IDictionary<string, string> ProducerConfigOverrides { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Raw <c>Confluent.Kafka</c> consumer config keys merged into the
    /// <see cref="ConsumerConfig"/> the receiver builds — escape hatch for
    /// tuning a knob Edict has not yet surfaced. <c>AddEdictKafkaStreams</c>
    /// validates these at wiring time and throws
    /// <see cref="InvalidOperationException"/> if an entry would flip
    /// <c>enable.auto.commit</c> back on. The factory also re-stamps
    /// <c>enable.auto.commit=false</c> after merging, so a missed validation
    /// cannot silently advance offsets ahead of <c>HandleAsync</c>.
    /// </summary>
    public IDictionary<string, string> ConsumerConfigOverrides { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
