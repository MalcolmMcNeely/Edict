using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Receiver-side streaming conformance for a saga: a pointer-bearing
/// <c>EdictEventEnvelope</c> reaches <c>ClaimCheckSaga</c> via the real stream
/// and dispatches through the deferred <c>InvokeHandler</c> path. The saga's
/// staged <c>SendCommand</c> effect must survive that path — the follow-up
/// command lands at its handler and <c>Progress</c> is persisted (in the
/// reference persistence), exactly as for an in-band event.
/// </summary>
public abstract class SagaReceivesClaimCheckScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected SagaReceivesClaimCheckScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PointerEnvelope_ShouldDispatchSagaCommandAndPersistProgress()
    {
        var counterId = Guid.NewGuid();

        await _fixture.Sender.SendAsync(new IncrementClaimCheckCounterCommand(counterId, $"saga-{Guid.NewGuid():N}"));

        var tracker = _fixture.GrainFactory.GetGrain<IClaimCheckSagaTrackerProbe>(counterId);
        await ClaimCheckConsumerWaiters.WaitForSagaTrackerAsync(tracker);

        var saga = _fixture.GrainFactory.GetGrain<IClaimCheckSagaProgressProbe>(counterId);

        Assert.Equal(1, await tracker.GetReceivedAsync());
        Assert.Equal(1, await saga.GetHandledAsync());
    }
}
