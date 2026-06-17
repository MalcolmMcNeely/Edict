using Edict.Contracts.Commands;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// The per-entry exactly-once guarantee, stronger than the same-group
/// all-or-nothing case: in a batch of three publish effects, faulting only the
/// middle one leaves its two siblings done and re-runs neither, while the failed
/// effect redrains and lands exactly once. The controllable executor puts every
/// entry in its own singleton group, so each is acked or failed independently; an
/// index-addressed fault picks the middle effect by its drain ordinal. Bound
/// against a controllable-executor fixture with a minimal backoff, so the failed
/// effect's redrain converges through a probe tick with no clock gate.
/// </summary>
public abstract class PerEffectPartialFailureScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    readonly TFixture _fixture;

    protected PerEffectPartialFailureScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MiddleEffectFaults_SiblingsStayDone_FailedEffectRedrainsExactlyOnce()
    {
        // Arrange — fault only the second effect (zero-based ordinal 1) of the batch.
        var counterId = Guid.NewGuid();
        _fixture.OutboxFault.Reset();
        _fixture.OutboxFault.FailEffectIndex = 1;

        // Act — three raised events become three publish effects; the inline drain
        // publishes the first and third and faults the middle one.
        var result = await _fixture.Sender.SendAsync(new BatchIncrementCounterCommand(counterId, 3));

        var probe = _fixture.GrainFactory.GetGrain<ICounterProbe>(counterId);

        // Assert — accepted, exactly one effect faulted, and that one effect is the
        // only entry still Pending (its two siblings were acked, not re-run).
        Assert.IsType<EdictCommandResult.Accepted>(result);
        Assert.Equal(1, _fixture.OutboxFault.FailedAttempts);
        Assert.Equal(1, await probe.GetPendingOutboxCountAsync());

        // Act — drive the redrain. The index-addressed fault only matched ordinal 1
        // on the first pass, so the failed effect's next attempt advances past it
        // and publishes.
        for (var tick = 0; tick < 12 && await probe.GetPendingOutboxCountAsync() > 0; tick++)
        {
            await probe.ForceDrainViaReminderAsync();
        }

        Assert.Equal(0, await probe.GetPendingOutboxCountAsync());

        // Assert — the capture grain received each of the three events exactly once:
        // the failed effect redrained, and neither sibling was lost or re-run.
        var capture = _fixture.GrainFactory.GetGrain<ICounterEventCaptureGrain>(counterId);
        await OutboxProbeWaiters.WaitUntilAsync(async () => (await capture.GetCapturedEventsAsync()).Count == 3);
        var captured = await capture.GetCapturedEventsAsync();

        var newCounts = captured.OfType<CounterIncrementedEvent>()
            .Select(incremented => incremented.NewCount)
            .OrderBy(count => count)
            .ToList();
        Assert.Equal([1, 2, 3], newCounts);
    }
}
