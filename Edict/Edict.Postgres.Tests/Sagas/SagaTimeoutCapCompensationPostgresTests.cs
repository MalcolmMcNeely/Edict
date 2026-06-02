using Edict.Tests.Conformance.Sagas;

namespace Edict.Postgres.Tests.Sagas;

[Collection(PostgresClusterCollection.Name)]
public sealed class SagaTimeoutCapCompensationPostgresTests(PostgresClusterFixture fixture)
    : SagaTimeoutCapCompensationScenarios<PostgresClusterFixture>(fixture);
