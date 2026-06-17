using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Idempotency;

[Collection(AqsStreamingCollection.Name)]
public sealed class RingEvictionBoundaryTests : RingEvictionBoundaryScenarios<AqsStreamingFixture>
{
    public RingEvictionBoundaryTests(AqsStreamingFixture fixture) : base(fixture) { }
}
