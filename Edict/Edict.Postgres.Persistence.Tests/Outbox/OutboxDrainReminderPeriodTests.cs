using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Postgres.Persistence.Tests.Outbox;

[Collection(PostgresPersistenceControllableExecutorCollection.Name)]
public sealed class OutboxDrainReminderPeriodTests(PostgresPersistenceControllableExecutorFixture fixture)
    : OutboxDrainReminderPeriodScenarios<PostgresPersistenceControllableExecutorFixture>(fixture);
