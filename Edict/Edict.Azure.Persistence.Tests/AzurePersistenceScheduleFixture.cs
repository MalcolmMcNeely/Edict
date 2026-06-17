using Xunit;

namespace Edict.Azure.Persistence.Tests;

/// <summary>
/// Persistence-axis fixture whose silo runs on the base's virtual clock so the
/// schedule-survival scenario can push a schedule's due and deadline instants
/// past-due without a wall-clock wait, then prove the catch-up-on-activation path
/// fires the overdue work from real Azure grain storage after a reactivation.
/// </summary>
public sealed class AzurePersistenceScheduleFixture : AzurePersistenceFixtureBase
{
    protected override bool UsesVirtualClock => true;
}

[CollectionDefinition(Name)]
public sealed class AzurePersistenceScheduleCollection
    : ICollectionFixture<AzurePersistenceScheduleFixture>
{
    public const string Name = "AzurePersistenceSchedule";
}
