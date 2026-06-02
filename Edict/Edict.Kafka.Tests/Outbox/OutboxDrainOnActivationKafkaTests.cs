using Edict.Kafka.Tests.DeadLetter;
using Edict.Tests.Conformance.Outbox;

namespace Edict.Kafka.Tests.Outbox;

[Collection(KafkaOutboxControllableExecutorCollection.Name)]
public sealed class OutboxDrainOnActivationKafkaTests(KafkaOutboxControllableExecutorFixture fixture)
    : OutboxDrainOnActivationScenarios<KafkaOutboxControllableExecutorFixture>(fixture);
