using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.EventHandler;

[Collection(AqsStreamingCollection.Name)]
public sealed class EventHandlerHandlesPublishedEventTests : EventHandlerHandlesPublishedEventScenarios<AqsStreamingFixture>
{
    public EventHandlerHandlesPublishedEventTests(AqsStreamingFixture fixture) : base(fixture) { }
}
