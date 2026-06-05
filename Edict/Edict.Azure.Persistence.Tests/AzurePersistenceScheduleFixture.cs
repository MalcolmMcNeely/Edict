using Edict.Tests.Conformance.Persistence;

using Microsoft.Extensions.Time.Testing;

using Xunit;

namespace Edict.Azure.Persistence.Tests;

/// <summary>
/// Persistence-axis fixture whose silo runs on a <see cref="FakeTimeProvider"/> so
/// the schedule-survival scenario can push a schedule's due and deadline instants
/// past-due without a wall-clock wait, then prove the catch-up-on-activation path
/// fires the overdue work from real Azure grain storage after a reactivation.
/// </summary>
public sealed class AzurePersistenceScheduleFixture : AzurePersistenceFixtureBase, IScheduleClockFixture
{
    readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    protected override TimeProvider? ClockOverride => _clock;

    public void AdvanceClock(TimeSpan by) => _clock.Advance(by);
}

[CollectionDefinition(Name)]
public sealed class AzurePersistenceScheduleCollection
    : ICollectionFixture<AzurePersistenceScheduleFixture>
{
    public const string Name = "AzurePersistenceSchedule";
}
