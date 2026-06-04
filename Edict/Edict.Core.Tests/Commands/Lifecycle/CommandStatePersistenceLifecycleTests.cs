using Edict.Contracts.Commands;
using Edict.Core.Tests.TestSupport;

using Orleans;

using Xunit;

namespace Edict.Core.Tests.Commands.Lifecycle;

// Drives ValidateAndHandleAsync directly through the LifecycleProbe's
// hand-written methods (never the generated dispatch spine or the sender route
// table), so these prove the hand-written Edict.Core lifecycle: State persists
// whenever the handler completes — on Accepted and Rejected alike, and
// independent of whether an event was raised — while a throw discards the turn
// and a validator rejection writes nothing.
[Collection(SubstrateIndependentCollection.Name)]
public sealed class CommandStatePersistenceLifecycleTests
{
    readonly SubstrateIndependentClusterFixture _fixture;

    public CommandStatePersistenceLifecycleTests(SubstrateIndependentClusterFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AcceptedHandlerRaisingNoEvent_ShouldPersistStateMutation_AcrossForcedDeactivation()
    {
        var probe = _fixture.GrainFactory.GetGrain<ILifecycleProbe>(Guid.NewGuid());

        // Arrange — mutate State and raise nothing, accepting the command.
        Assert.IsType<EdictCommandResult.Accepted>(await probe.AcceptedMutateAsync(5));
        var activationBeforeDeactivation = await probe.GetActivationIdAsync();

        // Act — force the aggregate to deactivate, then read through a reactivation.
        await probe.DeactivateAsync();
        await WaitForReactivationAsync(probe, activationBeforeDeactivation);

        // Assert — the no-event mutation survived the reload from durable storage.
        Assert.Equal(5, await probe.GetCounterAsync());
    }

    [Fact]
    public async Task RejectedHandler_ShouldPersistStateMutation_AcrossForcedDeactivation()
    {
        var probe = _fixture.GrainFactory.GetGrain<ILifecycleProbe>(Guid.NewGuid());

        Assert.IsType<EdictCommandResult.Rejected>(await probe.RejectedMutateAsync(7));
        var activationBeforeDeactivation = await probe.GetActivationIdAsync();

        await probe.DeactivateAsync();
        await WaitForReactivationAsync(probe, activationBeforeDeactivation);

        Assert.Equal(7, await probe.GetCounterAsync());
    }

    [Fact]
    public async Task HandlerRaisingThenRejecting_ShouldStillPublishTheEvent()
    {
        var key = Guid.NewGuid();
        var probe = _fixture.GrainFactory.GetGrain<ILifecycleProbe>(key);

        Assert.IsType<EdictCommandResult.Rejected>(await probe.RaiseThenRejectAsync(delta: 0, eventValue: 6));

        var capture = _fixture.GrainFactory.GetGrain<ILifecycleEventCaptureGrain>(key);
        await WaitUntilAsync(async () => (await capture.GetCapturedValuesAsync()).Contains(6));
        Assert.Contains(6, await capture.GetCapturedValuesAsync());
    }

    [Fact]
    public async Task HandlerThrow_ShouldDiscardPartialMutationAndBufferedEvents()
    {
        var key = Guid.NewGuid();
        var probe = _fixture.GrainFactory.GetGrain<ILifecycleProbe>(key);

        // Arrange — a durable baseline of 3 with no event raised.
        await probe.AcceptedMutateAsync(3);

        // Act — a handler that mutates (+5), raises a sentinel event, then throws.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => probe.ThrowAfterMutateAndRaiseAsync(delta: 5, eventValue: 99));

        // Assert — the partial +5 was rolled back within the same turn (the
        // durable snapshot reloaded), leaving the in-memory counter at 3.
        Assert.Equal(3, await probe.GetCounterAsync());

        // A subsequent accepted-and-raised event is the barrier: once it lands on
        // the capture grain, the pipeline has flushed, so the absence of the
        // discarded sentinel is meaningful rather than merely not-yet-arrived.
        await probe.AcceptedRaiseAsync(delta: 0, eventValue: 7);
        var capture = _fixture.GrainFactory.GetGrain<ILifecycleEventCaptureGrain>(key);
        await WaitUntilAsync(async () => (await capture.GetCapturedValuesAsync()).Contains(7));
        Assert.DoesNotContain(99, await capture.GetCapturedValuesAsync());
    }

    [Fact]
    public async Task ValidatorRejection_ShouldNotApplyTheHandlerMutation_WhileAPassingCommandDoes()
    {
        // A rejected validator short-circuits before the handler runs, so its
        // +5 mutation is never applied and nothing is written.
        var rejectedProbe = _fixture.GrainFactory.GetGrain<ILifecycleProbe>(Guid.NewGuid());
        var rejected = Assert.IsType<EdictCommandResult.Rejected>(
            await rejectedProbe.ValidatedMutateAsync(delta: 5, shouldPass: false));
        Assert.Equal("probe_validation_failed", Assert.Single(rejected.Reasons).Code);

        var rejectedActivation = await rejectedProbe.GetActivationIdAsync();
        await rejectedProbe.DeactivateAsync();
        await WaitForReactivationAsync(rejectedProbe, rejectedActivation);
        Assert.Equal(0, await rejectedProbe.GetCounterAsync());

        // Contrast — a passing validator lets the handler run, and its mutation
        // persists across the same forced deactivation.
        var acceptedProbe = _fixture.GrainFactory.GetGrain<ILifecycleProbe>(Guid.NewGuid());
        Assert.IsType<EdictCommandResult.Accepted>(
            await acceptedProbe.ValidatedMutateAsync(delta: 5, shouldPass: true));

        var acceptedActivation = await acceptedProbe.GetActivationIdAsync();
        await acceptedProbe.DeactivateAsync();
        await WaitForReactivationAsync(acceptedProbe, acceptedActivation);
        Assert.Equal(5, await acceptedProbe.GetCounterAsync());
    }

    static Task WaitForReactivationAsync(ILifecycleProbe probe, Guid previousActivationId) =>
        WaitUntilAsync(async () => await probe.GetActivationIdAsync() != previousActivationId);

    static async Task WaitUntilAsync(Func<Task<bool>> condition, int timeoutSeconds = 20)
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
