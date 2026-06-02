using Edict.Tests.Conformance.Idempotency;

namespace Edict.Kafka.Tests.Idempotency;

[Collection(KafkaClusterCollection.Name)]
public sealed class UnhandledEventTypeRingSlotKafkaTests(KafkaClusterFixture fixture)
    : UnhandledEventTypeRingSlotScenarios<KafkaClusterFixture>(fixture);
