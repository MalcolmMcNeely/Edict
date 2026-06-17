using Edict.Tests.Conformance.Sagas;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis conformance for the saga absolute-lifetime cap firing
/// compensation: a fired cap runs <c>OnSagaTimeoutAsync</c>, which dispatches one
/// compensating Command, and the inline outbox drain routes it to its handler.
/// The trigger arrives over the real stream (inline and oversized/claim-check
/// variants); the cap fires from the in-memory reminder via the probe; the
/// reference persistence holds the saga's lifecycle state. The assertion observes
/// the durable, externally-visible outcome (the delivered compensation), not the
/// saga's internal lifecycle slot.
/// </summary>
public abstract class SagaTimeoutCapCompensationScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    const string Stream = "ConformanceSagaTimeoutWorkflow";

    readonly TFixture _fixture;

    protected SagaTimeoutCapCompensationScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public Task FiredCap_ShouldDeliverCompensation_WhenTriggerArrivesInline() =>
        FiredCapDeliversCompensationAsync(oversized: false);

    [Fact]
    public Task FiredCap_ShouldDeliverCompensation_WhenTriggerArrivesOversized() =>
        FiredCapDeliversCompensationAsync(oversized: true);

    [Fact]
    public async Task DoubleCapFire_ShouldDeliverCompensationExactlyOnce()
    {
        var workflowId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<ISagaTimeoutPublisher>(workflowId);

        await publisher.PublishAsync(Stream, new TimeoutTriggerEvent(workflowId)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        var saga = _fixture.GrainFactory.GetGrain<ITimeoutSagaProbe>(workflowId);
        await SagaTimeoutWaiters.PumpUntilAsync(_fixture.AdvanceClock, async () => await saga.GetHandledAsync() >= 1);

        _fixture.AdvanceClock(TimeSpan.FromSeconds(2));

        // Fire the cap twice. The first terminalises the saga and dispatches the one
        // compensating Command; the second sees an already-terminal saga, resolves to
        // a no-op, and never re-dispatches. The grain is turn-based, so the second
        // fire observes the first's durable TimedOut write.
        await saga.FireCapAsync();
        await saga.FireCapAsync();

        var tracker = _fixture.GrainFactory.GetGrain<ICompensationTrackerProbe>(workflowId);

        // Both fires are awaited, so all staging is done; gate on the saga's outbox
        // draining to quiescence rather than on the tracker reaching one. A tracker-count
        // gate would return on the first compensation while an erroneously-staged second
        // still sat un-drained, so the exactly-once assertion would pass blind to it.
        // Once Pending is zero, any second SendCommand has fully drained to the tracker.
        await SagaTimeoutWaiters.WaitUntilAsync(async () => await saga.GetPendingOutboxCountAsync() == 0);

        // Only one SendCommand effect was ever staged: the second fire saw an
        // already-terminal saga, resolved to a no-op, and added nothing.
        Assert.Equal(1, await tracker.GetReceivedAsync());
        Assert.Equal(workflowId, await tracker.GetLastWorkflowIdAsync());
    }

    async Task FiredCapDeliversCompensationAsync(bool oversized)
    {
        var workflowId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<ISagaTimeoutPublisher>(workflowId);

        var trigger = new TimeoutTriggerEvent(workflowId)
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        if (oversized)
        {
            await publisher.PublishOversizedAsync(Stream, trigger);
        }
        else
        {
            await publisher.PublishAsync(Stream, trigger);
        }

        // The cap arms only once the trigger is handled; wait for that before
        // advancing past the one-second cap and firing it through the probe.
        var saga = _fixture.GrainFactory.GetGrain<ITimeoutSagaProbe>(workflowId);
        await SagaTimeoutWaiters.PumpUntilAsync(_fixture.AdvanceClock, async () => await saga.GetHandledAsync() >= 1);

        _fixture.AdvanceClock(TimeSpan.FromSeconds(2));
        await saga.FireCapAsync();

        var tracker = _fixture.GrainFactory.GetGrain<ICompensationTrackerProbe>(workflowId);
        await SagaTimeoutWaiters.WaitUntilAsync(async () => await tracker.GetReceivedAsync() >= 1);

        Assert.Equal(1, await tracker.GetReceivedAsync());
        Assert.Equal(workflowId, await tracker.GetLastWorkflowIdAsync());
    }
}
