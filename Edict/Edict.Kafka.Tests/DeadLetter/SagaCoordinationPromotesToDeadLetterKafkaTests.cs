using Edict.Tests.Conformance.DeadLetter;

namespace Edict.Kafka.Tests.DeadLetter;

[Collection(KafkaOutboxControllableExecutorCollection.Name)]
public sealed class SagaCoordinationPromotesToDeadLetterKafkaTests(KafkaOutboxControllableExecutorFixture fixture)
    : SagaCoordinationPromotesToDeadLetterScenarios<KafkaOutboxControllableExecutorFixture>(fixture);
