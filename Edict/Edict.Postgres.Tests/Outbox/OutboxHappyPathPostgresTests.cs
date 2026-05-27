using Edict.Tests.Conformance.Outbox;

namespace Edict.Postgres.Tests.Outbox;

[Collection(PostgresClusterCollection.Name)]
public sealed class OutboxHappyPathPostgresTests(PostgresClusterFixture fixture)
    : OutboxHappyPathScenarios<PostgresClusterFixture>(fixture);
