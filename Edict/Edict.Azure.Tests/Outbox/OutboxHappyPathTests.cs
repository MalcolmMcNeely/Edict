using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace Edict.Azure.Tests.Outbox;

/// <summary>
/// End-to-end Outbox engine happy path on Azurite:
/// a stateful command commits {State, Outbox} in one write, the inline FIFO
/// drain publishes via <c>PublishEventExecutor</c> over the real Azure Queue
/// stream provider, and steady state holds zero pending plus no Reminder.
/// Lifted from <c>Edict.Core.Tests/Outbox/OutboxEngineTests</c> (cycles 1, 3,
/// and 5) — re-expressed against real transport so the proof exercises the
/// substrate the sample silo wires in production.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class OutboxHappyPathTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task Send_ShouldPublishRaisedEventThroughInlineOutboxDrain()
    {
        var counterId = Guid.NewGuid();

        var result = await fixture.Sender.Send(new AzureIncrementCounterCommand(counterId));

        Assert.IsType<EdictCommandResult.Accepted>(result);

        var events = await WaitForCounterEventsAsync(counterId);
        var incremented = Assert.IsType<AzureCounterIncrementedEvent>(Assert.Single(events));
        Assert.Equal(counterId, incremented.CounterId);
        Assert.Equal(1, incremented.NewCount);
    }

    [Fact]
    public async Task InlineDrain_ShouldPublishMultipleRaisedEventsInFifoOrder()
    {
        var counterId = Guid.NewGuid();

        await fixture.Sender.Send(new AzureBatchIncrementCounterCommand(counterId, Times: 5));

        var events = await WaitForCounterEventsAsync(counterId, expectedCount: 5);
        Assert.Equal(
            [1, 2, 3, 4, 5],
            events.OfType<AzureCounterIncrementedEvent>().Select(e => e.NewCount));
    }

    [Fact]
    public async Task SuccessfulInlineDrain_ShouldLeaveZeroPendingAndNoReminder()
    {
        var counterId = Guid.NewGuid();

        await fixture.Sender.Send(new AzureIncrementCounterCommand(counterId));

        var probe = fixture.Cluster.GrainFactory.GetGrain<IAzureCounterProbe>(counterId);
        Assert.Equal(0, await probe.GetPendingOutboxCountAsync());
        Assert.False(await probe.HasDrainReminderAsync());
    }

    async Task<IReadOnlyList<EdictEvent>> WaitForCounterEventsAsync(
        Guid counterId, int expectedCount = 1)
    {
        var captureGrain = fixture.Cluster.GrainFactory.GetGrain<IAzureCounterEventCaptureGrain>(counterId);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
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
