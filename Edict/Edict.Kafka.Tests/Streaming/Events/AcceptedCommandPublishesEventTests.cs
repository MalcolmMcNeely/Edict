using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Events;

[Collection(KafkaStreamingCollection.Name)]
public sealed class AcceptedCommandPublishesEventTests : AcceptedCommandPublishesEventScenarios<KafkaStreamingFixture>
{
    public AcceptedCommandPublishesEventTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
