using Edict.Contracts.Commands;
using Edict.Contracts.Events;

using Orleans;

using Xunit;

namespace Edict.Tests.Conformance.Outbox;

/// <summary>
/// End-to-end Outbox engine happy path against the bound substrate:
/// a stateful command commits {State, Outbox} in one write, the inline drain
/// publishes via the real <see cref="Core.Outbox.PublishEventExecutor"/>
/// over the substrate's stream provider, and steady state holds zero pending
/// plus no Reminder.
/// </summary>
public abstract class OutboxHappyPathScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected OutboxHappyPathScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Send_ShouldPublishRaisedEventThroughInlineOutboxDrain()
    {
        var counterId = Guid.NewGuid();

        var result = await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        Assert.IsType<EdictCommandResult.Accepted>(result);

        var events = await CounterEventWaiters.WaitForEventsAsync(_fixture.GrainFactory, counterId);
        var incremented = Assert.IsType<CounterIncrementedEvent>(Assert.Single(events));
        Assert.Equal(counterId, incremented.CounterId);
        Assert.Equal(1, incremented.NewCount);
    }

    [Fact]
    public async Task InlineDrain_ShouldPublishEveryRaisedEventInABatch()
    {
        // Per-aggregate causal order is not preserved by the at-least-once
        // stack — consumers must be reorder-tolerant — so the happy-path
        // proves "no events are lost" via set-equality on NewCount, not
        // list-equality.
        var counterId = Guid.NewGuid();

        await _fixture.Sender.SendAsync(new BatchIncrementCounterCommand(counterId, Times: 5));

        var events = await CounterEventWaiters.WaitForEventsAsync(
            _fixture.GrainFactory, counterId, expectedCount: 5);
        var observedCounts = events.OfType<CounterIncrementedEvent>().Select(e => e.NewCount).ToHashSet();
        Assert.Equal(new HashSet<int> { 1, 2, 3, 4, 5 }, observedCounts);
    }

    [Fact]
    public async Task SuccessfulInlineDrain_ShouldLeaveZeroPendingAndNoReminder()
    {
        var counterId = Guid.NewGuid();

        await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);
        Assert.Equal(0, await probe.GetPendingOutboxCountAsync());
        Assert.False(await probe.HasDrainReminderAsync());
    }
}

static class CounterEventWaiters
{
    public static async Task<IReadOnlyList<EdictEvent>> WaitForEventsAsync(
        IGrainFactory grainFactory, Guid counterId, int expectedCount = 1, int timeoutSeconds = 20)
    {
        var captureGrain = grainFactory.GetGrain<ICounterEventCaptureGrain>(counterId);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var events = await captureGrain.GetCapturedEventsAsync();
            if (events.Count >= expectedCount)
            {
                return events;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await captureGrain.GetCapturedEventsAsync();
    }
}

static class OutboxProbeWaiters
{
    public static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutSeconds = 20)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
