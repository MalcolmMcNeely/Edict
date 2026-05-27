using Edict.Tests.Conformance.DeadLetter;

using Xunit;

namespace Edict.Kafka.Tests.DeadLetter;

[Collection(KafkaAzureOutboxControllableExecutorCollection.Name)]
public sealed class HandlerFailurePromotesToDeadLetterKafkaAzureTests(KafkaAzureOutboxControllableExecutorFixture fixture)
    : HandlerFailurePromotesToDeadLetterScenarios<KafkaAzureOutboxControllableExecutorFixture>(fixture);
