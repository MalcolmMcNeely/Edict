using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Projections;

[Collection(KafkaStreamingCollection.Name)]
public sealed class ProjectionCursorReadOverStreamTests : ProjectionCursorReadOverStreamScenarios<KafkaStreamingFixture>
{
    public ProjectionCursorReadOverStreamTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
