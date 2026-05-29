namespace Edict.Kafka.Internal;

/// <summary>
/// Wiring-time validation of the raw <c>Confluent.Kafka</c> passthrough
/// dictionaries on <see cref="EdictKafkaStreamsOptions"/>. The framework
/// stamps a fixed set of broker-contract floors (producer <c>acks=all</c>
/// and <c>enable.idempotence=true</c>, consumer <c>enable.auto.commit=false</c>)
/// that callers must not downgrade — the at-least-once delivery + dedup-ring
/// strategy depends on them. Throws
/// <see cref="InvalidOperationException"/> at <c>AddEdictKafkaStreams</c>
/// time so misconfiguration surfaces at host build, not during the first
/// produce or poll.
/// </summary>
static class EdictKafkaContractFloors
{
    internal static void ValidateProducerOverrides(IDictionary<string, string> overrides)
    {
        if (overrides.TryGetValue("acks", out var acks) && !IsAcksAll(acks))
        {
            throw new InvalidOperationException(
                $"EdictKafkaStreamsOptions.ProducerConfigOverrides[\"acks\"] = \"{acks}\" downgrades the Edict broker contract. acks must remain \"all\" — the at-least-once delivery guarantee depends on it.");
        }

        if (overrides.TryGetValue("enable.idempotence", out var idempotence) && !IsTrue(idempotence))
        {
            throw new InvalidOperationException(
                $"EdictKafkaStreamsOptions.ProducerConfigOverrides[\"enable.idempotence\"] = \"{idempotence}\" downgrades the Edict broker contract. The producer must stay idempotent — Edict's dedup ring assumes no producer-side duplicates inside a single send retry sequence.");
        }
    }

    internal static void ValidateConsumerOverrides(IDictionary<string, string> overrides)
    {
        if (overrides.TryGetValue("enable.auto.commit", out var autoCommit) && !IsFalse(autoCommit))
        {
            throw new InvalidOperationException(
                $"EdictKafkaStreamsOptions.ConsumerConfigOverrides[\"enable.auto.commit\"] = \"{autoCommit}\" downgrades the Edict broker contract. Auto-commit must stay off — Edict commits offsets manually after HandleAsync returns so a mid-handler crash redelivers, not silently advances.");
        }
    }

    static bool IsAcksAll(string value) =>
        string.Equals(value, "all", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "-1", StringComparison.Ordinal);

    static bool IsTrue(string value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1", StringComparison.Ordinal);

    static bool IsFalse(string value) =>
        string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "0", StringComparison.Ordinal);
}
