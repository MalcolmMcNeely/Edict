using Confluent.Kafka;

using Edict.Kafka.Internal;

using Xunit;

namespace Edict.Kafka.Tests.Configuration;

public sealed class EdictKafkaConsumerConfigFactoryTests
{
    [Fact]
    public void Build_ShouldApplyAutoOffsetResetFromOptions()
    {
        var options = new EdictKafkaStreamsOptions
        {
            BootstrapServers = "localhost:9092",
            ConsumerGroupId = "edict-test",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        };

        var config = EdictKafkaConsumerConfigFactory.Build(options, clientId: "test-client");

        Assert.Equal(AutoOffsetReset.Earliest, config.AutoOffsetReset);
    }
}
