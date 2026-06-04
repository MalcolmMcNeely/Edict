using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.DeadLetter;

[Collection(PostgresPersistenceDeadLetterCollection.Name)]
public sealed class SagaCoordinationPromotesToDeadLetterTests(PostgresPersistenceDeadLetterFixture fixture)
    : SagaCoordinationPromotesToDeadLetterScenarios<PostgresPersistenceDeadLetterFixture>(fixture);
