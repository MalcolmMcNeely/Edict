using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.ClaimCheck;

[Collection(AqsClaimCheckStreamingCollection.Name)]
public sealed class ClaimCheckPayloadSizeMetricsTests : ClaimCheckPayloadSizeMetricsScenarios<AqsClaimCheckStreamingFixture>
{
    public ClaimCheckPayloadSizeMetricsTests(AqsClaimCheckStreamingFixture fixture) : base(fixture) { }
}
