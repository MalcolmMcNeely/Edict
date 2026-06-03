using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis conformance for the redeliver-after-throw contract: when a
/// consumer's handling turn faults out to the stream layer (here, the commit
/// write throws — an infrastructure fault is not dead-lettered, it drops the
/// dirty activation), the bound streaming provider must <strong>redeliver</strong>
/// the event so a clean activation applies it exactly once. The fault is only the
/// trigger: it rides the reference persistence (a controllable decorator over the
/// reference memory grain storage), and the assertion is purely a streaming
/// property — the applied count can only reach one if the real stream redelivered
/// after the faulted turn. The conjunction with a <em>real</em> store's fault mode
/// is bucket 4, not this battery.
/// </summary>
public abstract class RedeliverAfterThrowScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected RedeliverAfterThrowScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FaultedConsumerTurn_ShouldBeRedeliveredByStream_AndAppliedExactlyOnce()
    {
        ControllableGrainStorage.Reset();
        var aggregateId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<IStateWriteFaultPublisher>(aggregateId);
        var consumer = _fixture.GrainFactory.GetGrain<IStateWriteFaultConsumer>(aggregateId);
        var initialActivationId = await consumer.GetActivationIdAsync();

        var edictEvent = new StateWriteFaultEvent(aggregateId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        // Arrange — the commit write for this event's handling turn faults.
        ControllableGrainStorage.ShouldFailWrites = true;
        await publisher.PublishAsync(edictEvent);

        // The handler mutates the payload, then the commit write faults out to the
        // stream layer, so the message is not acked.
        await OutboxProbeWaiters.WaitUntilAsync(() => Task.FromResult(ControllableGrainStorage.FailedWrites >= 1));
        Assert.True(ControllableGrainStorage.FailedWrites >= 1);

        // Heal the store. The framework dropped the dirty activation; the changed
        // activation id confirms the reload from clean durable state.
        ControllableGrainStorage.ShouldFailWrites = false;
        await OutboxProbeWaiters.WaitUntilAsync(async () => await consumer.GetActivationIdAsync() != initialActivationId);
        Assert.NotEqual(initialActivationId, await consumer.GetActivationIdAsync());

        // The real stream redelivers after the visibility timeout; the clean
        // activation applies the payload exactly once — proving redelivery.
        await OutboxProbeWaiters.WaitUntilAsync(async () => await consumer.GetAppliedCountAsync() == 1);
        Assert.Equal(1, await consumer.GetAppliedCountAsync());
    }
}
