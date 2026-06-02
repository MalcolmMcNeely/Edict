using Edict.Kafka.Tests.DeadLetter;
using Edict.Tests.Conformance.Outbox;

namespace Edict.Kafka.Tests.Outbox;

[Collection(KafkaOutboxControllableExecutorCollection.Name)]
public sealed class OutboxDrainReminderPeriodKafkaTests(KafkaOutboxControllableExecutorFixture fixture)
    : OutboxDrainReminderPeriodScenarios<KafkaOutboxControllableExecutorFixture>(fixture);
