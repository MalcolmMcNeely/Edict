using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Azure.Streaming.Tests.EventHandler;

[Collection(AqsStreamingCollection.Name)]
public sealed class EventHandlerSpanStitchAcrossOutboxHopTests : EventHandlerSpanStitchAcrossOutboxHopScenarios<AqsStreamingFixture>
{
    public EventHandlerSpanStitchAcrossOutboxHopTests(AqsStreamingFixture fixture) : base(fixture) { }
}
