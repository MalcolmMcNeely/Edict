using System.Collections.Immutable;

using Edict.Core.Schedules;

using static VerifyXunit.Verifier;

namespace Edict.Core.Tests.Schedules;

public sealed class ScheduleSliceTests
{
    static readonly Guid SchedA = new("aaaaaaaa-0000-0000-0000-000000000001");
    static readonly Guid SchedB = new("bbbbbbbb-0000-0000-0000-000000000002");
    static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly TimeSpan Period = TimeSpan.FromSeconds(2);

    static ScheduleEntry Entry(Guid id, DateTimeOffset dueAt, TimeSpan period, DateTimeOffset? deadlineAt = null) => new()
    {
        ScheduleId = id,
        MessagePayload = [1, 2, 3],
        Period = period,
        DueAt = dueAt,
        DeadlineAt = deadlineAt,
    };

    [Fact]
    public void Active_ShouldBe_ImmutableList_ForStructuralSharing()
    {
        var slice = new ScheduleSlice().Add(Entry(SchedA, Start + Period, Period));

        Assert.IsType<ImmutableList<ScheduleEntry>>(slice.Active);
    }

    [Fact]
    public Task Add_ShouldAppendToActiveTail()
    {
        var slice = new ScheduleSlice()
            .Add(Entry(SchedA, Start + Period, Period))
            .Add(Entry(SchedB, Start + Period + Period, Period));

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public void DueEntries_ShouldReturnOnlyEntriesDueAtOrBeforeNow()
    {
        var slice = new ScheduleSlice()
            .Add(Entry(SchedA, Start + Period, Period))
            .Add(Entry(SchedB, Start + TimeSpan.FromMinutes(5), Period));

        var due = slice.DueEntries(Start + Period);

        Assert.Equal([SchedA], due.Select(entry => entry.ScheduleId).ToArray());
    }

    [Fact]
    public void SoonestDueAt_ShouldReturnEarliestDueInstantAcrossActiveSchedules()
    {
        var slice = new ScheduleSlice()
            .Add(Entry(SchedA, Start + TimeSpan.FromMinutes(5), Period))
            .Add(Entry(SchedB, Start + Period, Period));

        Assert.Equal(Start + Period, slice.SoonestDueAt);
    }

    [Fact]
    public void SoonestDueAt_ShouldBeNull_WhenNoActiveSchedules()
    {
        Assert.Null(new ScheduleSlice().SoonestDueAt);
    }

    [Fact]
    public Task Continue_ShouldAdvanceDueByOnePeriod_WhenExactlyOneElapsed()
    {
        var slice = new ScheduleSlice()
            .Add(Entry(SchedA, Start + Period, Period))
            .Continue(SchedA, Start + Period);

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task Continue_ShouldCoalesceMultipleElapsedPeriods_IntoSingleForwardJumpStrictlyAfterNow()
    {
        // The grain slept through ~3.5 periods past the due instant. Continue
        // must jump the cadence grid once to the first instant strictly after
        // now, not replay a backlog of catch-up fires.
        var dueAt = Start + Period;
        var now = dueAt + TimeSpan.FromTicks(Period.Ticks * 7 / 2);

        var slice = new ScheduleSlice()
            .Add(Entry(SchedA, dueAt, Period))
            .Continue(SchedA, now);

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task Continue_ShouldBeNoOp_WhenScheduleIdNotPresent()
    {
        var slice = new ScheduleSlice()
            .Add(Entry(SchedA, Start + Period, Period))
            .Continue(SchedB, Start + Period);

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task Complete_ShouldRemoveMatchingEntry_PreservingInsertionOrder()
    {
        var slice = new ScheduleSlice()
            .Add(Entry(SchedA, Start + Period, Period))
            .Add(Entry(SchedB, Start + Period, Period))
            .Complete(SchedA);

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public Task Complete_ShouldBeNoOp_WhenScheduleIdNotPresent()
    {
        var slice = new ScheduleSlice()
            .Add(Entry(SchedA, Start + Period, Period))
            .Complete(SchedB);

        return Verify(slice).DontScrubGuids().DontScrubDateTimes();
    }

    [Fact]
    public void TimedOutEntries_ShouldReturnOnlyCappedEntriesWhoseDeadlineIsNowOrPast()
    {
        var deadline = Start + TimeSpan.FromMinutes(1);
        var slice = new ScheduleSlice()
            .Add(Entry(SchedA, Start + Period, Period, deadlineAt: deadline))
            .Add(Entry(SchedB, Start + Period, Period, deadlineAt: Start + TimeSpan.FromHours(1)));

        var timedOut = slice.TimedOutEntries(deadline);

        Assert.Equal([SchedA], timedOut.Select(entry => entry.ScheduleId).ToArray());
    }

    [Fact]
    public void TimedOutEntries_ShouldExcludeUncappedEntries()
    {
        var slice = new ScheduleSlice()
            .Add(Entry(SchedA, Start + Period, Period, deadlineAt: null));

        Assert.Empty(slice.TimedOutEntries(Start + TimeSpan.FromDays(3650)));
    }

    [Fact]
    public void SoonestTimeoutAt_ShouldReturnEarliestDeadlineAcrossCappedSchedules()
    {
        var slice = new ScheduleSlice()
            .Add(Entry(SchedA, Start + Period, Period, deadlineAt: Start + TimeSpan.FromHours(1)))
            .Add(Entry(SchedB, Start + Period, Period, deadlineAt: Start + TimeSpan.FromMinutes(5)));

        Assert.Equal(Start + TimeSpan.FromMinutes(5), slice.SoonestTimeoutAt);
    }

    [Fact]
    public void SoonestTimeoutAt_ShouldBeNull_WhenNoCappedSchedules()
    {
        var slice = new ScheduleSlice()
            .Add(Entry(SchedA, Start + Period, Period, deadlineAt: null));

        Assert.Null(slice.SoonestTimeoutAt);
    }

    [Fact]
    public void Continue_ShouldNotResetDeadline_SoTheCapIsArmedOnce()
    {
        var deadline = Start + TimeSpan.FromMinutes(1);
        var slice = new ScheduleSlice()
            .Add(Entry(SchedA, Start + Period, Period, deadlineAt: deadline))
            .Continue(SchedA, Start + Period)
            .Continue(SchedA, Start + Period + Period);

        Assert.Equal(deadline, slice.Active.Single().DeadlineAt);
    }
}
