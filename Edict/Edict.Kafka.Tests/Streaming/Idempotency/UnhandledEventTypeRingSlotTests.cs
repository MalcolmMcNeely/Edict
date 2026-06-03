using Edict.Tests.Conformance.Streaming;

using Xunit;

namespace Edict.Kafka.Tests.Streaming.Idempotency;

[Collection(KafkaStreamingCollection.Name)]
public sealed class UnhandledEventTypeRingSlotTests : UnhandledEventTypeRingSlotScenarios<KafkaStreamingFixture>
{
    public UnhandledEventTypeRingSlotTests(KafkaStreamingFixture fixture) : base(fixture) { }
}
