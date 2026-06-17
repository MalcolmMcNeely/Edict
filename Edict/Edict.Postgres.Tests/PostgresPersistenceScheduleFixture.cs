using Xunit;

namespace Edict.Postgres.Tests;

/// <summary>
/// Persistence-axis fixture whose silo runs on the base's virtual clock so the
/// schedule-survival scenario can push a schedule's due and deadline instants
/// past-due without a wall-clock wait, then prove the catch-up-on-activation path
/// fires the overdue work from real Postgres grain storage after a reactivation.
/// </summary>
public sealed class PostgresPersistenceScheduleFixture : PostgresPersistenceFixtureBase
{
    protected override bool UsesVirtualClock => true;
}

[CollectionDefinition(Name)]
public sealed class PostgresPersistenceScheduleCollection
    : ICollectionFixture<PostgresPersistenceScheduleFixture>
{
    public const string Name = "PostgresPersistenceSchedule";
}
