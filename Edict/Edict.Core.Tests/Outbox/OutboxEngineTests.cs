using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.Tests.Grains;

namespace Edict.Core.Tests.Outbox;

// The Outbox engine end-to-end against in-memory streams + virtual clock
// (ADR 0016 — no Azurite). A stateful command commits {State, Outbox} in one
// write; the inline FIFO drain publishes via the keyed PublishEvent executor.
[Collection(EdictClusterCollection.Name)]
public sealed class OutboxEngineTests(EdictClusterFixture fixture)
{
    // Cycle 1 — tracer bullet: accepted stateful command publishes its raised
    // event through the inline outbox drain and Send returns Accepted.
    [Fact]
    public async Task Send_ShouldPublishRaisedEventThroughInlineOutboxDrain()
    {
        var counterId = Guid.NewGuid();

        var result = await fixture.Sender.Send(new IncrementCounterCommand(counterId));

        Assert.IsType<EdictCommandResult.Accepted>(result);

        var events = await WaitForCounterEventsAsync(counterId);
        var incremented = Assert.IsType<CounterIncrementedEvent>(Assert.Single(events));
        Assert.Equal(counterId, incremented.CounterId);
        Assert.Equal(1, incremented.NewCount);
    }

    // Cycle 2 — {State, Outbox} commit is one atomic grain-document write:
    // the State mutation survives a forced deactivation/reactivation, so a
    // second command sees the persisted count (a volatile field would reset).
    [Fact]
    public async Task State_ShouldSurviveDeactivation_ProvingAtomicEnvelopeCommit()
    {
        var counterId = Guid.NewGuid();
        var aggregate = fixture.Cluster.GrainFactory.GetGrain<ICounterProbe>(counterId);

        await fixture.Sender.Send(new IncrementCounterCommand(counterId));
        Assert.Equal(1, await aggregate.GetCountAsync());

        await aggregate.DeactivateAsync();
        await Task.Delay(TimeSpan.FromSeconds(1)); // let the activation drain

        await fixture.Sender.Send(new IncrementCounterCommand(counterId));

        Assert.Equal(2, await aggregate.GetCountAsync());

        var events = await WaitForCounterEventsAsync(counterId, expectedCount: 2);
        Assert.Equal([1, 2], events.OfType<CounterIncrementedEvent>().Select(e => e.NewCount));
    }

    // Cycle 3 — multiple events raised in one command drain FIFO, preserving
    // per-aggregate causal order (stop-at-head, ADR 0018).
    [Fact]
    public async Task InlineDrain_ShouldPublishMultipleRaisedEventsInFifoOrder()
    {
        var counterId = Guid.NewGuid();

        await fixture.Sender.Send(new BatchIncrementCounterCommand(counterId, Times: 5));

        var events = await WaitForCounterEventsAsync(counterId, expectedCount: 5);
        Assert.Equal(
            [1, 2, 3, 4, 5],
            events.OfType<CounterIncrementedEvent>().Select(e => e.NewCount));
    }

    // Cycle 5 — steady state holds zero reminders: a successful inline drain
    // leaves nothing pending and never registers (or leaves) a Reminder, so
    // the happy path stays entirely off the reminder subsystem (ADR 0018).
    [Fact]
    public async Task SuccessfulInlineDrain_ShouldLeaveZeroPendingAndNoReminder()
    {
        var counterId = Guid.NewGuid();

        await fixture.Sender.Send(new IncrementCounterCommand(counterId));

        var probe = fixture.Cluster.GrainFactory.GetGrain<ICounterProbe>(counterId);
        Assert.Equal(0, await probe.GetPendingOutboxCountAsync());
        Assert.False(await probe.HasDrainReminderAsync());
    }

    private async Task<IReadOnlyList<EdictEvent>> WaitForCounterEventsAsync(
        Guid counterId, int expectedCount = 1)
    {
        var captureGrain = fixture.Cluster.GrainFactory.GetGrain<ICounterEventCaptureGrain>(counterId);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var events = await captureGrain.GetCapturedEventsAsync();
            if (events.Count >= expectedCount)
                return events;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        return await captureGrain.GetCapturedEventsAsync();
    }
}
