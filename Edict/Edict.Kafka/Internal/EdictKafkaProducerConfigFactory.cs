using Confluent.Kafka;

namespace Edict.Kafka.Internal;

/// <summary>
/// Builds the <see cref="ProducerConfig"/> the adapter hands to
/// <c>ProducerBuilder</c>. Producer floors (<c>acks=all</c>, idempotent
/// producer on) are stamped in here rather than left to options so a
/// passthrough downgrade cannot weaken them — the contract-floor validation
/// in <c>AddEdictKafkaStreams</c> rejects passthrough keys that try.
/// </summary>
static class EdictKafkaProducerConfigFactory
{
    internal static ProducerConfig Build(EdictKafkaStreamsOptions options, string clientId) =>
        new()
        {
            BootstrapServers = options.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            CompressionType = options.Compression,
            LingerMs = 5,
            MessageSendMaxRetries = int.MaxValue,
            ClientId = clientId,
        };
}
