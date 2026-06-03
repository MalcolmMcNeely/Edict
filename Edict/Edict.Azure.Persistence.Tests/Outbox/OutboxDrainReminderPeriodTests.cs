using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests.Outbox;

[Collection(AzurePersistenceControllableExecutorCollection.Name)]
public sealed class OutboxDrainReminderPeriodTests(AzurePersistenceControllableExecutorFixture fixture)
    : OutboxDrainReminderPeriodScenarios<AzurePersistenceControllableExecutorFixture>(fixture);
