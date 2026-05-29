using Edict.Azure.Tests.Outbox;
using Edict.Tests.Conformance.DeadLetter;

namespace Edict.Azure.Tests.DeadLetter;

[Collection(AzureOutboxControllableExecutorCollection.Name)]
public sealed class SagaCoordinationPromotesToDeadLetterTests(AzureOutboxDeadLetterClusterFixture fixture)
    : SagaCoordinationPromotesToDeadLetterScenarios<AzureOutboxDeadLetterClusterFixture>(fixture);
