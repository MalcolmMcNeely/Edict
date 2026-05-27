using Confluent.Kafka;

using Edict.Kafka.Internal;

using Xunit;

namespace Edict.Kafka.Tests.Configuration;

public sealed class EdictKafkaProducerConfigFactoryTests
{
    [Fact]
    public void Build_ShouldApplyCompressionFromOptions()
    {
        var options = new EdictKafkaStreamsOptions
        {
            BootstrapServers = "localhost:9092",
            Compression = CompressionType.Gzip,
        };

        var config = EdictKafkaProducerConfigFactory.Build(options, clientId: "test-client");

        Assert.Equal(CompressionType.Gzip, config.CompressionType);
    }
}
