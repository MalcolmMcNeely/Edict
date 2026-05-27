using Edict.Tests.Conformance.Projections;

namespace Edict.Postgres.Tests.Projections;

[Collection(PostgresClusterCollection.Name)]
public sealed class TableProjectionIncrementsOnSubsequentEventPostgresTests(PostgresClusterFixture fixture)
    : TableProjectionIncrementsOnSubsequentEventScenarios<PostgresClusterFixture>(fixture);
