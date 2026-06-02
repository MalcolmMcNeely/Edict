using Edict.Kafka;

using Xunit;

namespace Edict.Architecture.Tests;

public class ConfigurationDocCoverageMatcherTests
{
    [Fact]
    public void DocContainingEveryPropertyName_LeavesNothingUndocumented()
    {
        // Arrange
        var docText = string.Join(
            " | ",
            "StreamProviderName", "BootstrapServers", "ConsumerGroupId", "PartitionCount",
            "PartitionCountByStream", "ReplicationFactor", "MinInSyncReplicas", "Compression",
            "MessageTimeout", "AutoOffsetReset", "ProducerConfigOverrides", "ConsumerConfigOverrides");

        // Act
        var undocumented = ConfigurationDocCoverage.FindUndocumentedProperties(typeof(EdictKafkaStreamsOptions), docText);

        // Assert
        Assert.Empty(undocumented);
    }

    [Fact]
    public void DocMissingOneProperty_ReportsExactlyThatProperty()
    {
        // Arrange — every name except Compression, which no other name contains as a substring
        var docText = string.Join(
            " | ",
            "StreamProviderName", "BootstrapServers", "ConsumerGroupId", "PartitionCount",
            "PartitionCountByStream", "ReplicationFactor", "MinInSyncReplicas",
            "MessageTimeout", "AutoOffsetReset", "ProducerConfigOverrides", "ConsumerConfigOverrides");

        // Act
        var undocumented = ConfigurationDocCoverage.FindUndocumentedProperties(typeof(EdictKafkaStreamsOptions), docText);

        // Assert
        Assert.Equal(["Compression"], undocumented);
    }

    [Fact]
    public void EmptyDoc_ReportsEveryPublicGetterPropertyAndOnlyThose()
    {
        // Arrange — an empty doc means every required property is undocumented, so
        // the result is exactly the selected property set. The literal includes the
        // get-only dictionaries and derived MinInSyncReplicas, and omits the
        // internal IsReplicationFactorExplicit, which is the whole selection contract.
        string[] everyPublicGetterProperty =
        [
            "AutoOffsetReset", "BootstrapServers", "Compression", "ConsumerConfigOverrides",
            "ConsumerGroupId", "MessageTimeout", "MinInSyncReplicas", "PartitionCount",
            "PartitionCountByStream", "ProducerConfigOverrides", "ReplicationFactor", "StreamProviderName",
        ];

        // Act
        var undocumented = ConfigurationDocCoverage.FindUndocumentedProperties(typeof(EdictKafkaStreamsOptions), "");

        // Assert
        Assert.Equal(everyPublicGetterProperty, undocumented.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void PropertyNameInProseRatherThanTable_CountsAsDocumented()
    {
        // Arrange
        var docText =
            "The provider polls every partition independently. Set ConsumerGroupId so silos share an " +
            "assignment, and reach for PartitionCountByStream when one stream runs hotter than the fleet.";

        // Act
        var undocumented = ConfigurationDocCoverage.FindUndocumentedProperties(typeof(EdictKafkaStreamsOptions), docText);

        // Assert
        Assert.DoesNotContain("ConsumerGroupId", undocumented);
        Assert.DoesNotContain("PartitionCountByStream", undocumented);
    }
}
