using Edict.Kafka.Tests.DeadLetter;
using Edict.Tests.Conformance.Outbox;

namespace Edict.Kafka.Tests.Outbox;

[Collection(KafkaOutboxControllableExecutorCollection.Name)]
public sealed class OutboxRecoveryAfterCrashKafkaTests(KafkaOutboxControllableExecutorFixture fixture)
    : OutboxRecoveryAfterCrashScenarios<KafkaOutboxControllableExecutorFixture>(fixture);
