using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Events;

[Collection(KafkaStreamingCollection.Name)]
public sealed class RejectedCommandPublishesNoEventTests : RejectedCommandPublishesNoEventScenarios<KafkaStreamingFixture>
{
    public RejectedCommandPublishesNoEventTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
