using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.EventHandler;

[Collection(KafkaStreamingCollection.Name)]
public sealed class EventHandlerHandlesPublishedEventTests : EventHandlerHandlesPublishedEventScenarios<KafkaStreamingFixture>
{
    public EventHandlerHandlesPublishedEventTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
