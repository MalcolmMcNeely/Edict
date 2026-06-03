using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.ClaimCheck;

[Collection(KafkaClaimCheckStreamingCollection.Name)]
public sealed class ClaimCheckPayloadSizeMetricsTests : ClaimCheckPayloadSizeMetricsScenarios<KafkaClaimCheckStreamingFixture>
{
    public ClaimCheckPayloadSizeMetricsTests(KafkaClaimCheckStreamingFixture fixture) : base(fixture) { }
}
