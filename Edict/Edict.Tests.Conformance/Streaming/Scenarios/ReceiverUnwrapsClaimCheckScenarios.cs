using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Receiver-side streaming conformance: a pointer-bearing
/// <c>EdictEventEnvelope</c> arrives at an <c>EdictEventHandler</c> via the real
/// stream. The consumer base's stream observer stages an
/// <c>OutboxEffectKind.InvokeHandler</c> entry; the engine's
/// <c>InvokeHandlerExecutor</c> fetches the body from the reference claim-check
/// store and dispatches the unwrapped inner event to <c>Handle</c>. The test
/// observes the handler receiving the original payload, end-to-end, with no
/// <c>EdictEventEnvelope</c> visible at the <c>Handle</c> seam.
/// </summary>
public abstract class ReceiverUnwrapsClaimCheckScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected ReceiverUnwrapsClaimCheckScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PointerEnvelope_ShouldUnwrapToInnerEvent_ForEdictEventHandler()
    {
        var counterId = Guid.NewGuid();
        var payload = $"unwrap-me-{Guid.NewGuid():N}";

        await _fixture.Sender.SendAsync(new IncrementClaimCheckCounterCommand(counterId, payload));

        var handler = _fixture.GrainFactory.GetGrain<IClaimCheckEventHandlerProbe>(counterId);
        var handled = await WaitForHandledAsync(handler);

        var inner = Assert.Single(handled);
        Assert.Equal(counterId, inner.CounterId);
        Assert.Equal(1, inner.NewCount);
        Assert.Equal(payload, inner.Payload);
    }

    static async Task<IReadOnlyList<ClaimCheckCounterIncrementedEvent>> WaitForHandledAsync(
        IClaimCheckEventHandlerProbe handler, int expectedCount = 1)
    {
        IReadOnlyList<ClaimCheckCounterIncrementedEvent> events = [];
        await ConformanceWaiters.WaitUntilAsync(async () => (events = await handler.GetHandledEventsAsync()).Count >= expectedCount);
        return events;
    }
}
