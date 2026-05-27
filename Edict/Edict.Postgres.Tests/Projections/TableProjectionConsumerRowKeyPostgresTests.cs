using Edict.Tests.Conformance.Projections;

namespace Edict.Postgres.Tests.Projections;

[Collection(PostgresClusterCollection.Name)]
public sealed class TableProjectionConsumerRowKeyPostgresTests(PostgresClusterFixture fixture)
    : TableProjectionConsumerRowKeyScenarios<PostgresClusterFixture>(fixture);
