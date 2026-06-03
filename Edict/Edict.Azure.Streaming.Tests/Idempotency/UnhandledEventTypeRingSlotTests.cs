using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Idempotency;

[Collection(AqsStreamingCollection.Name)]
public sealed class UnhandledEventTypeRingSlotTests : UnhandledEventTypeRingSlotScenarios<AqsStreamingFixture>
{
    public UnhandledEventTypeRingSlotTests(AqsStreamingFixture fixture) : base(fixture) { }
}
