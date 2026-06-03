using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Telemetry;

[Collection(KafkaStreamingCollection.Name)]
public sealed class EventTelemeterizedTagsOnSpansTests : EventTelemeterizedTagsOnSpansScenarios<KafkaStreamingFixture>
{
    public EventTelemeterizedTagsOnSpansTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
