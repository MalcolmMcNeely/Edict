using Edict.Tests.Conformance.Outbox;

namespace Edict.Azure.Tests.Outbox;

/// <summary>
/// Azurite binding for
/// <see cref="OutboxDrainReminderPeriodScenarios{TFixture}"/>.
/// </summary>
[Collection(AzureOutboxControllableExecutorCollection.Name)]
public sealed class OutboxDrainReminderPeriodTests
    : OutboxDrainReminderPeriodScenarios<AzureOutboxReminderPeriodClusterFixture>
{
    public OutboxDrainReminderPeriodTests(AzureOutboxReminderPeriodClusterFixture fixture) : base(fixture)
    {
    }
}
