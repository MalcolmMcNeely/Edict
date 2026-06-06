using Edict.Telemetry;
using Edict.Tests.Conformance.EventHandler;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis conformance that the deferred <c>edict.event.handle</c> span is
/// a new trace root carrying one <see cref="System.Diagnostics.ActivityLink"/> back
/// to the originating <c>edict.event.publish</c> span across the stream hop. Under
/// the per-turn model the consumer turn is its own bounded trace, so the link — not
/// a shared trace — is what survives the real stream hop, a substrate-dependent
/// property since the publish span's identity must ride the event across it.
/// </summary>
public abstract class EventHandlerSpanStitchAcrossOutboxHopScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected EventHandlerSpanStitchAcrossOutboxHopScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DeferredInvocationSpan_ShouldBeNewRootLinkingToPublishSpan_AcrossStreamHop()
    {
        var customerId = Guid.NewGuid();
        using var capture = new SpanCapture();

        await _fixture.Sender.SendAsync(new NotifyCustomerCommand(customerId, "welcome"));

        var publishSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Publish} CustomerNotifiedEvent",
            "publish span for CustomerNotifiedEvent");
        // Scope to the handle span that links to this publish — the link both
        // identifies it across the hop and is the property under test.
        var invocationSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Handle} CustomerNotifiedEvent"
                && activity.Links.Any(link => link.Context.SpanId == publishSpan.SpanId),
            "deferred handle span linking to the publish span for CustomerNotifiedEvent");

        // A new trace root, not a child of publish, but linked back to it.
        Assert.Equal(default, invocationSpan.ParentSpanId);
        Assert.NotEqual(publishSpan.TraceId, invocationSpan.TraceId);
        Assert.Equal(publishSpan.TraceId, invocationSpan.Links.Single().Context.TraceId);
    }
}
