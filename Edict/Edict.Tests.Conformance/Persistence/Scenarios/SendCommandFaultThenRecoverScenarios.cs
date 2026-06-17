using Edict.Core.Outbox;
using Edict.Tests.Conformance.Outbox;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// Outbox fault coverage is not PublishEvent-only: a <c>SendCommand</c> effect
/// (the relay a saga stages) faults and recovers the same way. A kind-addressed,
/// count-addressed fault fails the first <c>SendCommand</c> attempt while leaving
/// any publish effect untouched, then auto-heals so the relayed command lands
/// exactly once on the target counter. Bound against a controllable-executor
/// fixture — which now wraps the send-command executor as well as the publish one
/// — with a minimal backoff, so recovery converges through a probe tick with no
/// clock gate.
/// </summary>
public abstract class SendCommandFaultThenRecoverScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    readonly TFixture _fixture;

    protected SendCommandFaultThenRecoverScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SendCommandEffect_FaultsThenRecovers_RelaysExactlyOnce()
    {
        // Arrange — fault only the first SendCommand attempt; publish effects (the
        // target's own raised event) are left to succeed.
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        _fixture.OutboxFault.Reset();
        _fixture.OutboxFault.FailOnlyKind = OutboxEffectKind.SendCommand;
        _fixture.OutboxFault.FailUntilAttempt = 1;

        var source = _fixture.GrainFactory.GetGrain<ICounterProbe>(sourceId);
        await source.StageSendCommandAsync(targetId);

        // Act — drive the drain. The first attempt faults the SendCommand; the
        // count-addressed fault then heals, so a later tick relays the command.
        for (var tick = 0; tick < 12 && await source.GetPendingOutboxCountAsync() > 0; tick++)
        {
            await source.ForceDrainViaReminderAsync();
        }

        // Assert — the SendCommand faulted at least once, the source outbox drained,
        // and the relayed increment landed on the target exactly once.
        Assert.True(_fixture.OutboxFault.FailedAttempts >= 1);
        Assert.Equal(0, await source.GetPendingOutboxCountAsync());

        var target = _fixture.GrainFactory.GetGrain<ICounterProbe>(targetId);
        await OutboxProbeWaiters.WaitUntilAsync(async () => await target.GetCountAsync() == 1);
        Assert.Equal(1, await target.GetCountAsync());
    }
}
