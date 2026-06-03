using Edict.Contracts.Configuration;

using Xunit;

namespace Edict.Azure.Persistence.Tests;

/// <summary>
/// Persistence-axis fixture whose silo swaps the real publish executor for
/// <c>ControllableOutboxExecutor</c> at <c>OutboxMaxAttempts</c> = 2 so a
/// poisoned outbox entry promotes to a dead-letter row after two failed
/// attempts. Backs the dead-letter promotion scenarios (RCA fields, typed
/// exception names) and the promotion/pending-count metrics scenarios.
/// </summary>
public sealed class AzurePersistenceDeadLetterFixture : AzurePersistenceFixtureBase
{
    protected override bool ReplacePublishExecutorWithControllable => true;

    protected override Action<EdictOptions>? ConfigureOptions => options =>
    {
        options.OutboxMaxAttempts = 2;
        options.OutboxBaseDelay = TimeSpan.FromMilliseconds(200);
        options.OutboxJitterFraction = 0;
    };
}

[CollectionDefinition(Name)]
public sealed class AzurePersistenceDeadLetterCollection
    : ICollectionFixture<AzurePersistenceDeadLetterFixture>
{
    public const string Name = "AzurePersistenceDeadLetter";
}
