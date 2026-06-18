using Edict.Contracts.Commands;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// The persistence half of write-fault clean-reload: a grain-state write fault
/// during command handling must throw to the sender (the command is not
/// accepted) and leave the activation clean — the framework drops the dirty
/// activation on the write fault, so a retry starts from durable state rather
/// than re-applying on top of a half-applied turn. The re-activation is the
/// trigger and a sender retry (no stream redelivery — the dumb reference stream
/// suffices) applies the command exactly once. The consumer-side write-fault ∧
/// redelivery conjunction needs both real backends at once and lives in the
/// bucket-4 residue, not here. Bound against a fixture whose silo wraps the
/// <c>edict-state</c> grain-storage provider with
/// <see cref="ControllableGrainStorage"/>.
/// </summary>
public abstract class StateWriteFaultScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    readonly TFixture _fixture;

    protected StateWriteFaultScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CommandWriteFault_ThrowsToSender_DropsDirtyActivation_AndAppliesExactlyOnceOnRetry()
    {
        _fixture.StorageFault.Reset();
        var counterId = Guid.NewGuid();
        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);
        var initialActivationId = await probe.GetActivationIdAsync();

        // Arrange — the next grain-state write faults. Scope the fault to this
        // counter so a peer grain's write (e.g. an event handler's dedup commit)
        // can never consume it.
        _fixture.StorageFault.TargetGrainKey = counterId.ToString("N");
        _fixture.StorageFault.ShouldFailWrites = true;

        // Act — the handler increments in-memory state, then the commit write faults.
        await Assert.ThrowsAnyAsync<Exception>(
            () => _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId)));

        // Assert — the write faulted, so the command was not accepted and nothing committed.
        Assert.True(_fixture.StorageFault.FailedWrites >= 1);

        // Recover the substrate. The framework's deactivation drops the dirty
        // activation; wait for the reactivation that reloads clean durable state.
        _fixture.StorageFault.ShouldFailWrites = false;
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
    public async Task CountAddressedWriteFault_DropsDirtyActivation_ThenSecondWriteRecoversWithoutManualHeal()
    {
        _fixture.StorageFault.Reset();
        var counterId = Guid.NewGuid();
        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);
        var initialActivationId = await probe.GetActivationIdAsync();

        // Arrange — fault only the first grain-state write, so recovery is the
        // count-addressed auto-heal, not a flag the scenario flips back. Scope the
        // fault to this counter: the count is silo-wide otherwise, and a peer
        // grain's write (an event handler's dedup commit, delivered asynchronously
        // and unawaited by the producer) lands in the armed window and steals the
        // single fault, leaving the counter's own commit to succeed.
        _fixture.StorageFault.TargetGrainKey = counterId.ToString("N");
        _fixture.StorageFault.FailUntilWrite = 1;

        // Act — the first commit write faults; the command is not accepted.
        await Assert.ThrowsAnyAsync<Exception>(
            () => _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId)));

        // Assert — the write faulted, dropping the dirty activation.
        Assert.True(_fixture.StorageFault.FailedWrites >= 1);
        await OutboxProbeWaiters.WaitUntilAsync(async () => await probe.GetActivationIdAsync() != initialActivationId);

        // Act — retry against the clean activation; the second write is past the
        // count-addressed fault, so it succeeds with no manual heal.
        var retry = await _fixture.Sender.SendAsync(new IncrementCounterCommand(counterId));

        // Assert — accepted, applied exactly once, event published exactly once.
        Assert.IsType<EdictCommandResult.Accepted>(retry);
        Assert.Equal(1, await probe.GetCountAsync());

        var capture = _fixture.GrainFactory.GetGrain<ICounterEventCaptureGrain>(counterId);
        await OutboxProbeWaiters.WaitUntilAsync(async () => (await capture.GetCapturedEventsAsync()).Count == 1);
        var incremented = Assert.IsType<CounterIncrementedEvent>(Assert.Single(await capture.GetCapturedEventsAsync()));
        Assert.Equal(1, incremented.NewCount);
    }
}
