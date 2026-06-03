using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.ClaimCheck;

[Collection(AqsClaimCheckStreamingCollection.Name)]
public sealed class ReceiverUnwrapsClaimCheckTests : ReceiverUnwrapsClaimCheckScenarios<AqsClaimCheckStreamingFixture>
{
    public ReceiverUnwrapsClaimCheckTests(AqsClaimCheckStreamingFixture fixture) : base(fixture) { }
}
