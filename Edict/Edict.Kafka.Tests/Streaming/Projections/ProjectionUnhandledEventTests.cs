using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Projections;

[Collection(KafkaStreamingCollection.Name)]
public sealed class ProjectionUnhandledEventTests : ProjectionUnhandledEventScenarios<KafkaStreamingFixture>
{
    public ProjectionUnhandledEventTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
