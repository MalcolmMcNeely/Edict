using Edict.Tests.Conformance.Outbox;

namespace Edict.Postgres.Tests.Outbox;

[Collection(PostgresOutboxControllableExecutorCollection.Name)]
public sealed class OutboxRecoveryAfterCrashPostgresTests(PostgresOutboxControllableExecutorFixture fixture)
    : OutboxRecoveryAfterCrashScenarios<PostgresOutboxControllableExecutorFixture>(fixture);
