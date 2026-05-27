using Edict.Tests.Conformance.Sagas;

namespace Edict.Postgres.Tests.Sagas;

[Collection(PostgresClusterCollection.Name)]
public sealed class SagaSendCommandEffectDeliversPostgresTests(PostgresClusterFixture fixture)
    : SagaSendCommandEffectDeliversScenarios<PostgresClusterFixture>(fixture);
