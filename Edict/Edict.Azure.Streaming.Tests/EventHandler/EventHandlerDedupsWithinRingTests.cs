using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.EventHandler;

[Collection(AqsStreamingCollection.Name)]
public sealed class EventHandlerDedupsWithinRingTests : EventHandlerDedupsWithinRingScenarios<AqsStreamingFixture>
{
    public EventHandlerDedupsWithinRingTests(AqsStreamingFixture fixture) : base(fixture) { }
}
