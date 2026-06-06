using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Projections;

[Collection(KafkaStreamingCollection.Name)]
public sealed class ProjectionDeliverySerializationTests : ProjectionDeliverySerializationScenarios<KafkaStreamingFixture>
{
    public ProjectionDeliverySerializationTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
