using Edict.Contracts.Commands;

using Xunit;

namespace Edict.Tests.Conformance.Outbox;

/// <summary>
/// A grain-state write fault during command handling must throw to the sender
/// (the command is not accepted) and leave the activation clean: the framework
/// drops the dirty activation on the write fault, so a retry starts from durable
/// state rather than re-applying on top of a half-applied turn. Bound against a
/// fixture whose silo wraps the <c>edict-state</c> grain-storage provider with
/// <see cref="ControllableGrainStorage"/>; the static <c>ShouldFailWrites</c>
/// toggle faults the real substrate's write.
/// </summary>
public abstract class StateWriteFaultScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected StateWriteFaultScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CommandWriteFault_ThrowsToSender_DropsDirtyActivation_AndAppliesExactlyOnceOnRetry()
    {
        ControllableGrainStorage.Reset();
        var counterId = Guid.NewGuid();
        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);
        var initialActivationId = await probe.GetActivationIdAsync();

        // Arrange — the next grain-state write faults.
        ControllableGrainStorage.ShouldFailWrites = true;

        // Act — the handler increments in-memory state, then the commit write faults.
        await Assert.ThrowsAnyAsync<Exception>(
            () => _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId)));

        // Assert — the write faulted, so the command was not accepted and nothing committed.
        Assert.True(ControllableGrainStorage.FailedWrites >= 1);

        // Recover the substrate. The framework's deactivation drops the dirty
        // activation; wait for the reactivation that reloads clean durable state.
        ControllableGrainStorage.ShouldFailWrites = false;
        await OutboxProbeWaiters.WaitUntilAsync(async () => await probe.GetActivationIdAsync() != initialActivationId);
        Assert.NotEqual(initialActivationId, await probe.GetActivationIdAsync());

        // Act — retry against the clean activation.
        var retry = await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        // Assert — accepted, applied exactly once (no double-apply over the
        // half-applied turn), and the event published exactly once.
        Assert.IsType<EdictCommandResult.Accepted>(retry);
        Assert.Equal(1, await probe.GetCountAsync());

        var capture = _fixture.GrainFactory.GetGrain<ICounterEventCaptureGrain>(counterId);
        await OutboxProbeWaiters.WaitUntilAsync(async () => (await capture.GetCapturedEventsAsync()).Count == 1);
        var captured = await capture.GetCapturedEventsAsync();
        var incremented = Assert.IsType<CounterIncrementedEvent>(Assert.Single(captured));
        Assert.Equal(1, incremented.NewCount);
    }

    [Fact]
    public async Task EventConsumerWriteFault_DropsDirtyActivation_AndAppliesPayloadExactlyOnceOnRedelivery()
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

        // The handler mutates the payload, then the commit write faults.
        await OutboxProbeWaiters.WaitUntilAsync(() => Task.FromResult(ControllableGrainStorage.FailedWrites >= 1));
        Assert.True(ControllableGrainStorage.FailedWrites >= 1);

        // Recover the substrate. The framework drops the dirty activation, so the
        // half-applied payload mutation is discarded rather than re-applied; the
        // changed activation id confirms the reload from durable state.
        ControllableGrainStorage.ShouldFailWrites = false;
        await OutboxProbeWaiters.WaitUntilAsync(async () => await consumer.GetActivationIdAsync() != initialActivationId);
        Assert.NotEqual(initialActivationId, await consumer.GetActivationIdAsync());

        // The queue redelivers the event after the visibility timeout; the clean
        // activation applies it exactly once — no double-apply on top of the
        // half-applied turn.
        await OutboxProbeWaiters.WaitUntilAsync(async () => await consumer.GetAppliedCountAsync() == 1);
        Assert.Equal(1, await consumer.GetAppliedCountAsync());
    }
}
