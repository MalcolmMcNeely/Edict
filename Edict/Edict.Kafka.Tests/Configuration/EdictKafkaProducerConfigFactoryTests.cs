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

    [Fact]
    public void Build_ShouldApplyProducerConfigOverride()
    {
        var options = new EdictKafkaStreamsOptions { BootstrapServers = "localhost:9092" };
        options.ProducerConfigOverrides["linger.ms"] = "99";

        var config = EdictKafkaProducerConfigFactory.Build(options, clientId: "test-client");

        Assert.Equal(99, config.LingerMs);
    }

    [Fact]
    public void Build_ShouldKeepFloors_WhenOverridesTryToDowngradeThem()
    {
        var options = new EdictKafkaStreamsOptions { BootstrapServers = "localhost:9092" };
        options.ProducerConfigOverrides["acks"] = "1";
        options.ProducerConfigOverrides["enable.idempotence"] = "false";

        var config = EdictKafkaProducerConfigFactory.Build(options, clientId: "test-client");

        Assert.Equal(Acks.All, config.Acks);
        Assert.True(config.EnableIdempotence);
    }

    [Fact]
    public void Build_ShouldApplyDefaultMessageTimeoutOfThirtySeconds()
    {
        var options = new EdictKafkaStreamsOptions { BootstrapServers = "localhost:9092" };

        var config = EdictKafkaProducerConfigFactory.Build(options, clientId: "test-client");

        Assert.Equal(30_000, config.MessageTimeoutMs);
    }

    [Fact]
    public void Build_ShouldApplyMessageTimeoutFromOptions()
    {
        var options = new EdictKafkaStreamsOptions
        {
            BootstrapServers = "localhost:9092",
            MessageTimeout = TimeSpan.FromSeconds(45),
        };

        var config = EdictKafkaProducerConfigFactory.Build(options, clientId: "test-client");

        Assert.Equal(45_000, config.MessageTimeoutMs);
    }

    [Fact]
    public void Build_ShouldLetProducerConfigOverrideWinOverMessageTimeout()
    {
        var options = new EdictKafkaStreamsOptions
        {
            BootstrapServers = "localhost:9092",
            MessageTimeout = TimeSpan.FromSeconds(45),
        };
        options.ProducerConfigOverrides["message.timeout.ms"] = "5000";

        var config = EdictKafkaProducerConfigFactory.Build(options, clientId: "test-client");

        Assert.Equal(5_000, config.MessageTimeoutMs);
    }
}
