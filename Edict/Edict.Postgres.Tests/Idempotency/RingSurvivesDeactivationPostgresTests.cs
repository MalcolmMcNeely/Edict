using Edict.Tests.Conformance.Idempotency;

namespace Edict.Postgres.Tests.Idempotency;

[Collection(PostgresClusterCollection.Name)]
public sealed class RingSurvivesDeactivationPostgresTests(PostgresClusterFixture fixture)
    : RingSurvivesDeactivationScenarios<PostgresClusterFixture>(fixture);
