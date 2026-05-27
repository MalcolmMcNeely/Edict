using Edict.Tests.Conformance.Projections;

namespace Edict.Postgres.Tests.Projections;

[Collection(PostgresClusterCollection.Name)]
public sealed class ProjectionDeliveryPostgresTests(PostgresClusterFixture fixture)
    : ProjectionDeliveryScenarios<PostgresClusterFixture>(fixture);
