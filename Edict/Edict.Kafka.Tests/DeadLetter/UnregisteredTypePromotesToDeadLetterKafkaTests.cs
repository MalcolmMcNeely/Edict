using Edict.Tests.Conformance.DeadLetter;

namespace Edict.Kafka.Tests.DeadLetter;

[Collection(KafkaOutboxControllableExecutorCollection.Name)]
public sealed class UnregisteredTypePromotesToDeadLetterKafkaTests(KafkaOutboxControllableExecutorFixture fixture)
    : UnregisteredTypePromotesToDeadLetterScenarios<KafkaOutboxControllableExecutorFixture>(fixture);
