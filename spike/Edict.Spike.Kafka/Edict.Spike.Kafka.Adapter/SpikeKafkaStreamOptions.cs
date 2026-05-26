using Confluent.Kafka;

namespace Edict.Spike.Kafka.Adapter;

public sealed class SpikeKafkaStreamOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string Topic { get; set; } = "spike.orders";
    public int PartitionCount { get; set; } = 4;
    public string ConsumerGroup { get; set; } = "spike-edict-silo";
    public TimeSpan PollTimeout { get; set; } = TimeSpan.FromMilliseconds(200);
    public AutoOffsetReset AutoOffsetReset { get; set; } = AutoOffsetReset.Latest;
}
