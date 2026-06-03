using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.EventHandler;

[Collection(KafkaStreamingCollection.Name)]
public sealed class EventHandlerSpanStitchAcrossOutboxHopTests : EventHandlerSpanStitchAcrossOutboxHopScenarios<KafkaStreamingFixture>
{
    public EventHandlerSpanStitchAcrossOutboxHopTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
