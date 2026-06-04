using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Persistence.Tests.DeadLetter;

[Collection(PostgresPersistenceDeadLetterCollection.Name)]
public sealed class HandlerFailurePromotesToDeadLetterTests(PostgresPersistenceDeadLetterFixture fixture)
    : HandlerFailurePromotesToDeadLetterScenarios<PostgresPersistenceDeadLetterFixture>(fixture);
