using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.DeadLetter;

[Collection(AzurePersistenceDeadLetterCollection.Name)]
public sealed class SagaCoordinationPromotesToDeadLetterTests(AzurePersistenceDeadLetterFixture fixture)
    : SagaCoordinationPromotesToDeadLetterScenarios<AzurePersistenceDeadLetterFixture>(fixture);
