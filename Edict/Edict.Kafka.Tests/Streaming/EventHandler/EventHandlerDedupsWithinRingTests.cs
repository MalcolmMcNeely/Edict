using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.EventHandler;

[Collection(KafkaStreamingCollection.Name)]
public sealed class EventHandlerDedupsWithinRingTests : EventHandlerDedupsWithinRingScenarios<KafkaStreamingFixture>
{
    public EventHandlerDedupsWithinRingTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
