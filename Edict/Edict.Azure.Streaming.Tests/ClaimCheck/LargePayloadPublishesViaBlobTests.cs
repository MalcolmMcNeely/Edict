using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.ClaimCheck;

[Collection(AqsClaimCheckStreamingCollection.Name)]
public sealed class LargePayloadPublishesViaBlobTests : LargePayloadPublishesViaBlobScenarios<AqsClaimCheckStreamingFixture>
{
    public LargePayloadPublishesViaBlobTests(AqsClaimCheckStreamingFixture fixture) : base(fixture) { }
}
