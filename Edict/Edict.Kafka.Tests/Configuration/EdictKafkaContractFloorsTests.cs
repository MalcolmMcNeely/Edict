using Edict.Kafka.Internal;

using Xunit;

namespace Edict.Kafka.Tests.Configuration;

public sealed class EdictKafkaContractFloorsTests
{
    [Theory]
    [InlineData("acks", "1")]
    [InlineData("acks", "0")]
    [InlineData("acks", "quorum")]
    public void ValidateProducerOverrides_ShouldThrow_WhenAcksIsNotAll(string key, string value)
    {
        var overrides = new Dictionary<string, string> { [key] = value };

        var exception = Assert.Throws<InvalidOperationException>(
            () => EdictKafkaContractFloors.ValidateProducerOverrides(overrides));

        Assert.Contains("acks", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("enable.idempotence", "false")]
    [InlineData("enable.idempotence", "FALSE")]
    [InlineData("enable.idempotence", "0")]
    public void ValidateProducerOverrides_ShouldThrow_WhenIdempotenceIsOff(string key, string value)
    {
        var overrides = new Dictionary<string, string> { [key] = value };

        var exception = Assert.Throws<InvalidOperationException>(
            () => EdictKafkaContractFloors.ValidateProducerOverrides(overrides));

        Assert.Contains("idempotence", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateProducerOverrides_ShouldNotThrow_WhenFloorsAreMatchedRedundantly()
    {
        var overrides = new Dictionary<string, string>
        {
            ["acks"] = "all",
            ["enable.idempotence"] = "true",
        };

        EdictKafkaContractFloors.ValidateProducerOverrides(overrides);
    }

    [Fact]
    public void ValidateProducerOverrides_ShouldNotThrow_WhenOverrideKeyIsUnrelatedToFloors()
    {
        var overrides = new Dictionary<string, string> { ["linger.ms"] = "20" };

        EdictKafkaContractFloors.ValidateProducerOverrides(overrides);
    }

    [Theory]
    [InlineData("enable.auto.commit", "true")]
    [InlineData("enable.auto.commit", "TRUE")]
    [InlineData("enable.auto.commit", "1")]
    public void ValidateConsumerOverrides_ShouldThrow_WhenAutoCommitIsOn(string key, string value)
    {
        var overrides = new Dictionary<string, string> { [key] = value };

        var exception = Assert.Throws<InvalidOperationException>(
            () => EdictKafkaContractFloors.ValidateConsumerOverrides(overrides));

        Assert.Contains("auto.commit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateConsumerOverrides_ShouldNotThrow_WhenFloorMatchedRedundantly()
    {
        var overrides = new Dictionary<string, string> { ["enable.auto.commit"] = "false" };

        EdictKafkaContractFloors.ValidateConsumerOverrides(overrides);
    }

    [Fact]
    public void ValidateConsumerOverrides_ShouldNotThrow_WhenOverrideKeyIsUnrelatedToFloors()
    {
        var overrides = new Dictionary<string, string> { ["fetch.min.bytes"] = "1024" };

        EdictKafkaContractFloors.ValidateConsumerOverrides(overrides);
    }
}
