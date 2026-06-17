using Edict.Contracts.Configuration;

using Xunit;

namespace Edict.Azure.Persistence.Tests;

/// <summary>
/// Persistence-axis fixture whose silo swaps the real publish and send-command
/// executors for <c>ControllableOutboxExecutor</c>, at <c>OutboxMaxAttempts</c> = 2
/// with a minimal backoff. The fine-grained outbox-fault scenarios fault a single
/// attempt, a single effect in a batch, or only the <c>SendCommand</c> kind, then
/// converge the entry through repeated reminder-drain probe ticks — a tick's grain
/// round-trip outlasts the 1 ms backoff, so recovery needs no wall-clock gate.
/// </summary>
public sealed class AzurePersistenceOutboxFaultGranularityFixture : AzurePersistenceFixtureBase
{
    protected override bool ReplacePublishExecutorWithControllable => true;

    protected override Action<EdictOptions>? ConfigureOptions => options =>
    {
        options.OutboxMaxAttempts = 2;
        options.OutboxBaseDelay = TimeSpan.FromMilliseconds(1);
        options.OutboxJitterFraction = 0;
        options.OutboxDrainReminderPeriod = TimeSpan.FromMinutes(2);
    };
}

[CollectionDefinition(Name)]
public sealed class AzurePersistenceOutboxFaultGranularityCollection
    : ICollectionFixture<AzurePersistenceOutboxFaultGranularityFixture>
{
    public const string Name = "AzurePersistenceOutboxFaultGranularity";
}
