using Edict.Contracts.Configuration;

using Xunit;

namespace Edict.Postgres.Persistence.Tests;

/// <summary>
/// Persistence-axis fixture whose silo swaps the real publish executor for
/// <c>ControllableOutboxExecutor</c> so the outbox recovery, drain-on-activation,
/// and lazy-reminder scenarios can fault the post-commit publish and prove the
/// pending entry survives on real Postgres until a reminder-driven drain
/// finishes it. The reminder period is long enough that the test observes the
/// registered reminder before it auto-fires; deterministic backoff keeps the
/// recovery timing stable.
/// </summary>
public sealed class PostgresPersistenceControllableExecutorFixture : PostgresPersistenceFixtureBase
{
    protected override bool ReplacePublishExecutorWithControllable => true;

    protected override Action<EdictOptions>? ConfigureOptions => options =>
    {
        options.OutboxBaseDelay = TimeSpan.FromMilliseconds(200);
        options.OutboxJitterFraction = 0;
        options.OutboxDrainReminderPeriod = TimeSpan.FromMinutes(2);
    };
}

[CollectionDefinition(Name)]
public sealed class PostgresPersistenceControllableExecutorCollection
    : ICollectionFixture<PostgresPersistenceControllableExecutorFixture>
{
    public const string Name = "PostgresPersistenceControllableExecutor";
}
