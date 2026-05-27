using Edict.Tests.Conformance.Outbox;

namespace Edict.Postgres.Tests.Outbox;

[Collection(PostgresClusterCollection.Name)]
public sealed class OutboxStateAtomicityPostgresTests(PostgresClusterFixture fixture)
    : OutboxStateAtomicityScenarios<PostgresClusterFixture>(fixture);
