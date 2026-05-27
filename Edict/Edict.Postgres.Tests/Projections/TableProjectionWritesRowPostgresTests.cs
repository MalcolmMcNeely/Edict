using Edict.Tests.Conformance.Projections;

namespace Edict.Postgres.Tests.Projections;

[Collection(PostgresClusterCollection.Name)]
public sealed class TableProjectionWritesRowPostgresTests(PostgresClusterFixture fixture)
    : TableProjectionWritesRowScenarios<PostgresClusterFixture>(fixture);
