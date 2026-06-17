using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Idempotency;

[Collection(AqsStreamingCollection.Name)]
public sealed class ExactlyOnceUnderRedeliveryTests : ExactlyOnceUnderRedeliveryScenarios<AqsStreamingFixture>
{
    public ExactlyOnceUnderRedeliveryTests(AqsStreamingFixture fixture) : base(fixture) { }
}
