using Edict.Tests.Conformance.Projections;

namespace Edict.Postgres.Tests.Projections;

[Collection(PostgresClusterCollection.Name)]
public sealed class ProjectionUnhandledEventPostgresTests(PostgresClusterFixture fixture)
    : ProjectionUnhandledEventScenarios<PostgresClusterFixture>(fixture);
