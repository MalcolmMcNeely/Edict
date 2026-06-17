using Edict.Telemetry;
using Edict.Tests.Conformance;
using Edict.Tests.Conformance.EventHandler;

namespace Edict.Azure.Streaming.Tests.Telemetry;

[Collection(AqsStreamingCollection.Name)]
public sealed class CommandPublishHandleSpanStitchTests(AqsStreamingFixture fixture)
{
    [Fact]
    public async Task CommandPublishParentChild_AndHandleLinksToPublish_AcrossAzureQueueHop()
    {
        var customerId = Guid.NewGuid();
        using var capture = new SpanCapture();

        await fixture.Sender.SendAsync(new NotifyCustomerCommand(customerId, "welcome"));

        var commandSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Command} NotifyCustomerCommand"
                && customerId.ToString("N").Equals(activity.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)),
            "command span for NotifyCustomerCommand");
        var publishSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Publish} CustomerNotifiedEvent"
                && activity.ParentSpanId == commandSpan.SpanId,
            "publish span for CustomerNotifiedEvent");
        // Scope to the handle span that links to this publish — the link both
        // identifies it across the hop and is the property under test.
        var handleSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Handle} CustomerNotifiedEvent"
                && activity.Links.Any(link => link.Context.SpanId == publishSpan.SpanId),
            "deferred handle span linking to the publish span for CustomerNotifiedEvent");

        // Command -> publish stays one parent-child trace (a synchronous turn).
        Assert.Equal(commandSpan.TraceId, publishSpan.TraceId);

        // The consumer turn is its own trace, linked back to publish across the hop.
        Assert.Equal(default, handleSpan.ParentSpanId);
        Assert.NotEqual(commandSpan.TraceId, handleSpan.TraceId);
        Assert.Equal(publishSpan.TraceId, handleSpan.Links.Single().Context.TraceId);
    }
}
