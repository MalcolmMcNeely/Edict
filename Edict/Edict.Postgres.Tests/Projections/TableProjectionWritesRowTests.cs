using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Tests.Projections;

[Collection(PostgresPersistenceCollection.Name)]
public sealed class TableProjectionWritesRowTests(PostgresPersistenceFixture fixture)
    : TableProjectionWritesRowScenarios<PostgresPersistenceFixture>(fixture);
