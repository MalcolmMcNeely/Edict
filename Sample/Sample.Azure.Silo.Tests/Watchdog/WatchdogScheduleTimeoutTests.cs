using Edict.Testing;

using Sample.Contracts.Watchdog.Commands;
using Sample.Contracts.Watchdog.Events;
using Sample.Domain.Watchdog.CommandHandlers;

using Xunit;

namespace Sample.Azure.Silo.Tests.Watchdog;

/// <summary>
/// Drives the EdictSchedule timeout surface end-to-end through the in-memory Test
/// Framework: StartWatchdog schedules a poll with a timeout cap, and the
/// interval-agnostic FireScheduleTimeoutsAsync advances the virtual clock to the
/// cap and fires it, so the OnScheduleTimeoutAsync compensation lands on the
/// Timeline without waiting on wall-clock time.
/// </summary>
public sealed class WatchdogScheduleTimeoutTests
{
    [Fact]
    public async Task StartWatchdog_WhenItsCapFires_RunsOnScheduleTimeout_RaisingEscalationOntoTheTimeline()
    {
        var watchdogId = Guid.Parse("a0000000-0000-0000-0000-000000000001");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(WatchdogCommandHandler).Assembly));

        await app.SendAsync(new StartWatchdogCommand(watchdogId));
        await app.Drain();

        // Interval-agnostic: advance to the cap and fire the timeout.
        await app.FireScheduleTimeoutsAsync();

        var escalated = app.Timeline.Entries.Count(entry =>
            entry.Kind == "Event" && entry.Type == nameof(WatchdogEscalatedEvent));

        Assert.Equal(1, escalated);

        // The cap is terminal — a second timeout fire is a no-op.
        await app.FireScheduleTimeoutsAsync();
        Assert.Equal(1, app.Timeline.Entries.Count(entry =>
            entry.Kind == "Event" && entry.Type == nameof(WatchdogEscalatedEvent)));
    }
}
