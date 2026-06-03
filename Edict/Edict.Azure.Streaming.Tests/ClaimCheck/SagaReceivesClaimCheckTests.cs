using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.ClaimCheck;

[Collection(AqsClaimCheckStreamingCollection.Name)]
public sealed class SagaReceivesClaimCheckTests : SagaReceivesClaimCheckScenarios<AqsClaimCheckStreamingFixture>
{
    public SagaReceivesClaimCheckTests(AqsClaimCheckStreamingFixture fixture) : base(fixture) { }
}
