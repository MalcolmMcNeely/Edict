using Xunit;

namespace Edict.Postgres.Tests;

/// <summary>
/// Persistence-axis fixture whose silo runs on the base's virtual clock so the
/// saga-timeout compensation-failure scenario can push a saga past its absolute
/// lifetime cap without a wall-clock wait, then prove the throwing
/// <c>OnSagaTimeoutAsync</c> converges to a single dead-letter row on real Postgres
/// grain storage and the Postgres dead-letter table.
/// </summary>
public sealed class PostgresPersistenceSagaTimeoutFixture : PostgresPersistenceFixtureBase
{
    protected override bool UsesVirtualClock => true;
}

[CollectionDefinition(Name)]
public sealed class PostgresPersistenceSagaTimeoutCollection
    : ICollectionFixture<PostgresPersistenceSagaTimeoutFixture>
{
    public const string Name = "PostgresPersistenceSagaTimeout";
}
