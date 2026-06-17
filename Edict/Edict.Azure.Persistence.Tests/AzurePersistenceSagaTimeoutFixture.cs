using Xunit;

namespace Edict.Azure.Persistence.Tests;

/// <summary>
/// Persistence-axis fixture whose silo runs on the base's virtual clock so the
/// saga-timeout compensation-failure scenario can push a saga past its absolute
/// lifetime cap without a wall-clock wait, then prove the throwing
/// <c>OnSagaTimeoutAsync</c> converges to a single dead-letter row on real Azure
/// grain storage and the literal Azure dead-letter table.
/// </summary>
public sealed class AzurePersistenceSagaTimeoutFixture : AzurePersistenceFixtureBase
{
    protected override bool UsesVirtualClock => true;
}

[CollectionDefinition(Name)]
public sealed class AzurePersistenceSagaTimeoutCollection
    : ICollectionFixture<AzurePersistenceSagaTimeoutFixture>
{
    public const string Name = "AzurePersistenceSagaTimeout";
}
