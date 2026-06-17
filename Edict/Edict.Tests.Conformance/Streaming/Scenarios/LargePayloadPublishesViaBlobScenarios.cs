using Edict.Contracts.Events;
using Edict.Tests.Conformance.ClaimCheck;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Pointer-branch streaming conformance: with the silo's claim-check policy at a
/// 1-byte threshold, any raised event must publish as an
/// <see cref="EdictEventEnvelope"/> on the pointer branch (null
/// <c>InlinePayload</c>), while the body lands in the reference claim-check store
/// keyed by the envelope's <c>EventId</c> — observed via the fixture's existence
/// probe, never by asserting a durable-store property.
/// </summary>
public abstract class LargePayloadPublishesViaBlobScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected LargePayloadPublishesViaBlobScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RaisedEvent_ShouldPublishAsPointerEnvelope_AndUploadBodyToBlob()
    {
        var counterId = Guid.NewGuid();
        var payload = new string('x', 64);

        await _fixture.Sender.SendAsync(new IncrementClaimCheckCounterCommand(counterId, payload));

        var capture = _fixture.GrainFactory.GetGrain<IClaimCheckEventCaptureGrain>(counterId);
        var captured = await WaitForCapturedAsync(capture);

        var envelope = Assert.IsType<EdictEventEnvelope>(Assert.Single(captured));
        Assert.Null(envelope.InlinePayload);
        Assert.NotEqual(Guid.Empty, envelope.EventId);
        Assert.Equal("ConformanceClaimCheckCounters", envelope.InnerEventStreamName);
        Assert.Equal(counterId.ToString("N"), envelope.InnerEventRouteKey);

        var blobExists = await _fixture.ClaimCheckBlobExistsAsync(envelope.EventId);
        Assert.True(blobExists,
            $"claim-check body for event '{envelope.EventId:N}' must exist in the fixture's claim-check store.");
    }

    static async Task<IReadOnlyList<EdictEvent>> WaitForCapturedAsync(IClaimCheckEventCaptureGrain capture)
    {
        IReadOnlyList<EdictEvent> events = [];
        await ConformanceWaiters.WaitUntilAsync(async () => (events = await capture.GetCapturedEventsAsync()).Count > 0);
        return events;
    }
}
