using Confluent.Kafka;

namespace Edict.Kafka.Internal;

/// <summary>
/// Builds the <see cref="ConsumerConfig"/> the receiver hands to
/// <c>ConsumerBuilder</c>. The consumer floor (<c>enable.auto.commit=false</c>
/// — manual commit deferred to <see cref="EdictKafkaReceiver.MessagesDeliveredAsync"/>)
/// is stamped here so a passthrough downgrade cannot weaken it; the
/// contract-floor validation in <c>AddEdictKafkaStreams</c> rejects passthrough
/// keys that try.
/// </summary>
static class EdictKafkaConsumerConfigFactory
{
    internal static ConsumerConfig Build(EdictKafkaStreamsOptions options, string clientId) =>
        new()
        {
            BootstrapServers = options.BootstrapServers,
            GroupId = options.ConsumerGroupId,
            EnableAutoCommit = false,
            AutoOffsetReset = options.AutoOffsetReset,
            EnablePartitionEof = false,
            ClientId = clientId,
            AllowAutoCreateTopics = false,
        };
}
