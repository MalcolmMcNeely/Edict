using Edict.Contracts.Configuration;

using Xunit;

namespace Edict.Postgres.Persistence.Tests;

/// <summary>
/// Persistence-axis fixture whose silo wraps the real <c>edict-state</c>
/// Postgres grain-storage provider with <c>ControllableGrainStorage</c> so the
/// write-fault clean-reload scenario can fault the real substrate's grain-state
/// write and prove the framework drops the dirty activation. Keeps the shipped
/// publish executor so the recovery publish reaches the reference stream.
/// </summary>
public sealed class PostgresPersistenceStateWriteFaultFixture : PostgresPersistenceFixtureBase
{
    protected override bool DecorateGrainStorage => true;

    protected override Action<EdictOptions>? ConfigureOptions => options =>
    {
        options.OutboxBaseDelay = TimeSpan.FromMilliseconds(200);
        options.OutboxJitterFraction = 0;
    };
}

[CollectionDefinition(Name)]
public sealed class PostgresPersistenceStateWriteFaultCollection
    : ICollectionFixture<PostgresPersistenceStateWriteFaultFixture>
{
    public const string Name = "PostgresPersistenceStateWriteFault";
}
