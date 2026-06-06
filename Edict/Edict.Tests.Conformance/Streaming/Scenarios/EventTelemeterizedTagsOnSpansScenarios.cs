using Edict.Telemetry;
using Edict.Tests.Conformance.EventHandler;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis conformance that <c>[EdictTelemeterized]</c> on an event
/// property lands on both the producer-side <c>edict.event.publish</c> span and
/// the consumer-side <c>edict.event.handle</c> span, across the real stream hop.
/// </summary>
public abstract class EventTelemeterizedTagsOnSpansScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected EventTelemeterizedTagsOnSpansScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PublishAndHandleSpans_ShouldCarryTelemeterizedEventTag()
    {
        var customerId = Guid.NewGuid();
        const string reason = "promo-trace-tag";
        using var capture = new SpanCapture();

        await _fixture.Sender.SendAsync(new NotifyCustomerCommand(customerId, reason));

        var publishSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Publish} CustomerNotifiedEvent"
                && reason.Equals(activity.GetTagItem("edict.reason")),
            "publish span for CustomerNotifiedEvent");
        var handleSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Handle} CustomerNotifiedEvent"
                && reason.Equals(activity.GetTagItem("edict.reason")),
            "event handle span for CustomerNotifiedEvent");

        Assert.Equal(reason, publishSpan.GetTagItem("edict.reason"));
        Assert.Equal(reason, handleSpan.GetTagItem("edict.reason"));
    }
}
