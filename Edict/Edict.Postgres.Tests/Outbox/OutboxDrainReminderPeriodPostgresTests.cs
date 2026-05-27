using Edict.Tests.Conformance.Outbox;

namespace Edict.Postgres.Tests.Outbox;

[Collection(PostgresOutboxControllableExecutorCollection.Name)]
public sealed class OutboxDrainReminderPeriodPostgresTests(PostgresOutboxControllableExecutorFixture fixture)
    : OutboxDrainReminderPeriodScenarios<PostgresOutboxControllableExecutorFixture>(fixture);
