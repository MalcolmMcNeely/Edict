using Confluent.Kafka;

using Xunit;

using static VerifyXunit.Verifier;

namespace Edict.Kafka.Tests.Configuration;

public sealed class EdictKafkaStreamsOptionsTests
{
    [Fact]
    public Task Construct_ShouldExposeDocumentedDefaults()
    {
        var options = new EdictKafkaStreamsOptions();

        // AutoOffsetReset.Latest is default(AutoOffsetReset), which Verify
        // scrubs from the snapshot — pinned here so a regression to Earliest
        // still trips a test.
        Assert.Equal(AutoOffsetReset.Latest, options.AutoOffsetReset);

        return Verify(options);
    }

    [Theory]
    [InlineData((short)1, (short)1)]
    [InlineData((short)3, (short)2)]
    public void MinInSyncReplicas_ShouldDeriveFromReplicationFactor(short replicationFactor, short expected)
    {
        var options = new EdictKafkaStreamsOptions { ReplicationFactor = replicationFactor };

        Assert.Equal(expected, options.MinInSyncReplicas);
    }

    [Fact]
    public void PartitionCountFor_ShouldReturnGlobalDefault_WhenStreamHasNoOverride()
    {
        var options = new EdictKafkaStreamsOptions { PartitionCount = 32 };

        Assert.Equal(32, options.PartitionCountFor("orders"));
    }

    [Fact]
    public void PartitionCountFor_ShouldReturnOverride_WhenStreamPresentInDictionary()
    {
        var options = new EdictKafkaStreamsOptions { PartitionCount = 32 };
        options.PartitionCountByStream["orders"] = 128;

        Assert.Equal(128, options.PartitionCountFor("orders"));
    }

    [Fact]
    public void ConfigOverrideDictionaries_ShouldRoundTripKeyValuePairs()
    {
        var options = new EdictKafkaStreamsOptions();
        options.ProducerConfigOverrides["linger.ms"] = "10";
        options.ConsumerConfigOverrides["fetch.min.bytes"] = "1024";

        Assert.Equal("10", options.ProducerConfigOverrides["linger.ms"]);
        Assert.Equal("1024", options.ConsumerConfigOverrides["fetch.min.bytes"]);
    }
}
