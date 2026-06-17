using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Idempotency;

[Collection(KafkaStreamingCollection.Name)]
public sealed class RingEvictionBoundaryTests : RingEvictionBoundaryScenarios<KafkaStreamingFixture>
{
    public RingEvictionBoundaryTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
