using Edict.Tests.Conformance.Projections;

namespace Edict.Postgres.Tests.Projections;

[Collection(PostgresClusterCollection.Name)]
public sealed class TableProjectionSingletonPostgresTests(PostgresClusterFixture fixture)
    : TableProjectionSingletonScenarios<PostgresClusterFixture>(fixture);
