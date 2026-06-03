using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Projections;

[Collection(KafkaStreamingCollection.Name)]
public sealed class ProjectionDeliveryTests : ProjectionDeliveryScenarios<KafkaStreamingFixture>
{
    public ProjectionDeliveryTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
