using Edict.Contracts.Configuration;

using Xunit;

namespace Edict.Postgres.Tests;

/// <summary>
/// Persistence-axis fixture whose silo swaps the real publish executor for
/// <c>ControllableOutboxExecutor</c> at <c>OutboxMaxAttempts</c> = 2 so a
/// poisoned outbox entry promotes to a dead-letter row after two failed
/// attempts. Backs the dead-letter promotion scenarios (RCA fields, typed
/// exception names) and the promotion/pending-count metrics scenarios.
/// </summary>
public sealed class PostgresPersistenceDeadLetterFixture : PostgresPersistenceFixtureBase
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
public sealed class PostgresPersistenceDeadLetterCollection
    : ICollectionFixture<PostgresPersistenceDeadLetterFixture>
{
    public const string Name = "PostgresPersistenceDeadLetter";
}
