using Edict.Testing;

using Sample.Contracts.Countdown.Commands;
using Sample.Contracts.Countdown.Events;
using Sample.Domain.Countdown.CommandHandlers;

using Xunit;

namespace Sample.Azure.Silo.Tests.Countdown;

/// <summary>
/// Drives the EdictSchedule surface end-to-end through the in-memory Test
/// Framework: StartCountdown schedules a tick from inside HandleAsync, and each
/// interval-agnostic FireDueSchedulesAsync drives one fire that routes back to
/// HandleAsync(CountdownTick), raising its events onto the Timeline. Continue
/// re-arms; Complete stops.
/// </summary>
public sealed class CountdownScheduleTests
{
    [Fact]
    public async Task StartCountdown_FiredToCompletion_RaisesATickPerStepThenFinishes_AndThenStops()
    {
        var countdownId = Guid.Parse("c0000000-0000-0000-0000-000000000001");

        await using var app = await EdictTestApp.StartAsync(b => b
            .WithConsumer(typeof(CountdownCommandHandler).Assembly));

        await app.SendAsync(new StartCountdownCommand(countdownId, From: 2));
        await app.Drain();

        // Two fires walk the countdown to zero, each interval-agnostic.
        await app.FireDueSchedulesAsync();
        await app.FireDueSchedulesAsync();

        var ticked = app.Timeline.Entries.Count(entry =>
            entry.Kind == "Event" && entry.Type == nameof(CountdownTickedEvent));
        var finished = app.Timeline.Entries.Count(entry =>
            entry.Kind == "Event" && entry.Type == nameof(CountdownFinishedEvent));

        Assert.Equal(2, ticked);
        Assert.Equal(1, finished);

        // A third fire is a no-op — Complete() stopped the schedule.
        await app.FireDueSchedulesAsync();
        Assert.Equal(2, app.Timeline.Entries.Count(entry =>
            entry.Kind == "Event" && entry.Type == nameof(CountdownTickedEvent)));
    }
}
