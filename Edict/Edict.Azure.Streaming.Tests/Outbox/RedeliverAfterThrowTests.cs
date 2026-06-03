using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Outbox;

[Collection(AqsStreamingCollection.Name)]
public sealed class RedeliverAfterThrowTests : RedeliverAfterThrowScenarios<AqsStreamingFixture>
{
    public RedeliverAfterThrowTests(AqsStreamingFixture fixture) : base(fixture) { }
}
