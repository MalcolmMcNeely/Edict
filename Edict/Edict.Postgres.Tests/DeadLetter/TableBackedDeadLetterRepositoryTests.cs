using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.DeadLetter;

[Collection(PostgresPersistenceCollection.Name)]
public sealed class TableBackedDeadLetterRepositoryTests(PostgresPersistenceFixture fixture)
    : TableBackedDeadLetterRepositoryScenarios<PostgresPersistenceFixture>(fixture);
