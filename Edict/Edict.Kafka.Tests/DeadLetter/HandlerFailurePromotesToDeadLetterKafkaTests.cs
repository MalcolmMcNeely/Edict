using Edict.Tests.Conformance.DeadLetter;

using Xunit;

namespace Edict.Kafka.Tests.DeadLetter;

[Collection(KafkaOutboxControllableExecutorCollection.Name)]
public sealed class HandlerFailurePromotesToDeadLetterKafkaTests(KafkaOutboxControllableExecutorFixture fixture)
    : HandlerFailurePromotesToDeadLetterScenarios<KafkaOutboxControllableExecutorFixture>(fixture);
