using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Persistence.Tests.DeadLetter;

[Collection(PostgresPersistenceCollection.Name)]
public sealed class TableBackedDeadLetterRepositoryTests(PostgresPersistenceFixture fixture)
    : TableBackedDeadLetterRepositoryScenarios<PostgresPersistenceFixture>(fixture);
