using Edict.Tests.Conformance.Outbox;

namespace Edict.Postgres.Tests.Outbox;

[Collection(PostgresOutboxControllableExecutorCollection.Name)]
public sealed class OutboxDrainOnActivationPostgresTests(PostgresOutboxControllableExecutorFixture fixture)
    : OutboxDrainOnActivationScenarios<PostgresOutboxControllableExecutorFixture>(fixture);
