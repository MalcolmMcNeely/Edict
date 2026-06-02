using Edict.Tests.Conformance.Sagas;

namespace Edict.Postgres.Tests.Sagas;

[Collection(PostgresClusterCollection.Name)]
public sealed class SagaTimeoutTerminalDeadLetterPostgresTests(PostgresClusterFixture fixture)
    : SagaTimeoutTerminalDeadLetterScenarios<PostgresClusterFixture>(fixture);
