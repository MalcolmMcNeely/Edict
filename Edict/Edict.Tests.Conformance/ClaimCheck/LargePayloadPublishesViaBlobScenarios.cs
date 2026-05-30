using Edict.Contracts.Events;

using Xunit;

namespace Edict.Tests.Conformance.ClaimCheck;

/// <summary>
/// Pointer-branch conformance: with the silo's claim-check policy at a
/// 1-byte threshold, any raised event must publish as an
/// <see cref="EdictEventEnvelope"/> carrying a <c>ClaimCheckKey</c> and a
/// null <c>InlinePayload</c>, while the body lands in the substrate's
/// claim-check store.
/// </summary>
public abstract class LargePayloadPublishesViaBlobScenarios<TFixture>
    where TFixture : ClaimCheckFixture
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
        Assert.False(string.IsNullOrEmpty(envelope.ClaimCheckKey),
            "publisher must populate ClaimCheckKey when ClaimCheckPolicy takes the pointer branch.");
        Assert.Null(envelope.InlinePayload);
        Assert.Equal("ConformanceClaimCheckCounters", envelope.InnerEventStreamName);
        Assert.Equal(counterId, envelope.InnerEventRouteKey);

        var blobExists = await _fixture.ClaimCheckBlobExistsAsync(envelope.ClaimCheckKey!);
        Assert.True(blobExists,
            $"claim-check blob '{envelope.ClaimCheckKey}' must exist in the fixture's claim-check store.");
    }

    static async Task<IReadOnlyList<EdictEvent>> WaitForCapturedAsync(
        IClaimCheckEventCaptureGrain capture, int timeoutSeconds = 15)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var events = await capture.GetCapturedEventsAsync();
            if (events.Count > 0)
            {
                return events;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await capture.GetCapturedEventsAsync();
    }
}
