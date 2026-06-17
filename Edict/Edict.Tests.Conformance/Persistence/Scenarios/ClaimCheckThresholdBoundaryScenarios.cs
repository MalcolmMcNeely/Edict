using Edict.Core.DeadLetter;
using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// The publish-side claim-check size comparator at its boundary: a payload whose
/// serialized event is exactly the threshold stays inline; one byte over spills to
/// a pointer. Catches the regression that flips <c>&lt;=</c> to <c>&lt;</c>. The
/// silo's threshold is the serialized size of a reference-length probe event
/// stamped at the fixture's frozen epoch, so a reference-length payload lands
/// exactly on it and one character more crosses it — both authored explicitly. The
/// decision is observed on the real claim-check store: the inline event parks no
/// body (a fetch by its id raises the typed miss), the over event parks one (the
/// fetch returns it verbatim). Pinning <c>OccurredAt</c> to the frozen epoch — the
/// only variable-width field — is what makes the boundary deterministic, so this
/// fixture runs on the virtual clock and pumps delivery rather than waiting.
/// </summary>
public abstract class ClaimCheckThresholdBoundaryScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture, IClaimCheckStoreFixture
{
    readonly TFixture _fixture;

    protected ClaimCheckThresholdBoundaryScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ExactlyAtThreshold_StaysInline_OneByteOver_GoesViaPointer()
    {
        // Arrange — two payloads one serialized byte apart around the threshold.
        var atThresholdPayload = new string('x', ClaimCheckThresholdProbe.ReferencePayloadLength);
        var overThresholdPayload = new string('x', ClaimCheckThresholdProbe.ReferencePayloadLength + 1);
        var atProbeId = Guid.NewGuid();
        var overProbeId = Guid.NewGuid();

        // Both commands are handled before the clock is pumped, so both events are
        // stamped at the frozen epoch — the same instant the threshold was computed
        // against, which is what makes the serialized sizes line up.
        await _fixture.Sender.SendAsync(new IncrementThresholdProbeCommand(atProbeId, atThresholdPayload));
        await _fixture.Sender.SendAsync(new IncrementThresholdProbeCommand(overProbeId, overThresholdPayload));

        // Act — pump the frozen clock so the reference stream delivers both events to
        // their probe handlers, which report the framework-assigned EventIds back.
        var atHandler = _fixture.GrainFactory.GetGrain<IThresholdProbeHandler>(atProbeId);
        var overHandler = _fixture.GrainFactory.GetGrain<IThresholdProbeHandler>(overProbeId);
        await ClaimCheckThresholdWaiters.PumpUntilAsync(_fixture.AdvanceClock, async () =>
            await atHandler.GetReceivedEventIdAsync() is not null
            && await overHandler.GetReceivedEventIdAsync() is not null);

        var atEventId = await atHandler.GetReceivedEventIdAsync();
        var overEventId = await overHandler.GetReceivedEventIdAsync();
        Assert.NotNull(atEventId);
        Assert.NotNull(overEventId);

        // The premise of the size determinism: the silo stamped both at the epoch
        // the threshold was computed against.
        Assert.Equal(PersistenceConformanceFixture.VirtualClockEpoch, (await atHandler.GetReceivedOccurredAtAsync())!.Value);
        Assert.Equal(PersistenceConformanceFixture.VirtualClockEpoch, (await overHandler.GetReceivedOccurredAtAsync())!.Value);

        // Assert — exactly-at-threshold stayed inline: no body parked, so a fetch by
        // its id raises the typed miss. One byte over spilled: its body is in the
        // store, returned verbatim by the same id.
        await Assert.ThrowsAsync<EdictClaimCheckFetchException>(
            () => _fixture.GetClaimCheckAsync(tenant: null, atEventId!.Value, CancellationToken.None));

        var parked = await _fixture.GetClaimCheckAsync(tenant: null, overEventId!.Value, CancellationToken.None);
        Assert.NotEmpty(parked.ToArray());
    }
}
