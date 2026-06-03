using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.EventHandler;

[Collection(KafkaStreamingCollection.Name)]
public sealed class EventHandlerNoOpForUnhandledTypeTests : EventHandlerNoOpForUnhandledTypeScenarios<KafkaStreamingFixture>
{
    public EventHandlerNoOpForUnhandledTypeTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
