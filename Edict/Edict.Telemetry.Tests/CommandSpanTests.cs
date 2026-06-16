using System.Diagnostics;

using Edict.Contracts.Events;

namespace Edict.Telemetry.Tests;

/// <summary>
/// Integration tests: the Command → Publish → Handle span tree.
/// Asserts EdictDiagnostics lives in Edict.Telemetry and the span shape is intact.
/// </summary>
[Collection(TelemetryClusterCollection.Name)]
public sealed class CommandSpanTests(TelemetryClusterFixture fixture)
{
    [Fact]
    public async Task Send_ShouldOpenOneEdictSpanPerCommandDispatch()
    {
        var orderId = Guid.NewGuid();
        using var capture = new SpanCapture();

        await fixture.Sender.SendAsync(new TelPlaceOrderCommand(orderId, "SKU-1"));

        await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Command} TelPlaceOrderCommand"
                && orderId.ToString("N").Equals(activity.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)),
            "command span for TelPlaceOrderCommand");

        var commandSpans = capture.Snapshot().Where(activity =>
            activity.OperationName == $"{SemanticConventions.Commands.Spans.Command} TelPlaceOrderCommand"
            && orderId.ToString("N").Equals(activity.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)));
        Assert.Single(commandSpans);
    }

    [Fact]
    public async Task Send_ShouldRecordErrorStatus_WhenHandlerThrows()
    {
        var orderId = Guid.NewGuid();
        using var capture = new SpanCapture();

        await Assert.ThrowsAnyAsync<Exception>(
            () => fixture.Sender.SendAsync(new TelFailOrderCommand(orderId)));

        var span = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Command} TelFailOrderCommand"
                && orderId.ToString("N").Equals(activity.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)),
            "command span for TelFailOrderCommand");
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public async Task Send_ShouldWriteTelemeterizedPropertiesAsEdictTags()
    {
        var orderId = Guid.NewGuid();
        const string sku = "SKU-TELEM-1";
        using var capture = new SpanCapture();

        await fixture.Sender.SendAsync(new TelPlaceOrderCommand(orderId, sku));

        var span = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Command} TelPlaceOrderCommand"
                && orderId.ToString("N").Equals(activity.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)),
            "command span for TelPlaceOrderCommand");
        Assert.Equal(sku, span.GetTagItem("edict.sku"));
    }

    [Fact]
    public async Task CommandHandleSpan_ShouldBeChildOfWebCommandSpan_OnApiPath()
    {
        var orderId = Guid.NewGuid();
        using var capture = new SpanCapture();

        await fixture.Sender.SendAsync(new TelPlaceOrderCommand(orderId, "SKU-1"));

        var commandSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Command} TelPlaceOrderCommand"
                && orderId.ToString("N").Equals(activity.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)),
            "command span for TelPlaceOrderCommand");
        var handleSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Handle} TelPlaceOrderCommand"
                && orderId.ToString("N").Equals(activity.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)),
            "command handle span for TelPlaceOrderCommand");

        Assert.Equal(commandSpan.TraceId, handleSpan.TraceId);
        Assert.Equal(commandSpan.SpanId, handleSpan.ParentSpanId);
    }

    [Fact]
    public async Task CommandHandleSpan_ShouldCarryRouteKeyTag()
    {
        var orderId = Guid.NewGuid();
        using var capture = new SpanCapture();

        await fixture.Sender.SendAsync(new TelPlaceOrderCommand(orderId, "SKU-1"));

        var handleSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Handle} TelPlaceOrderCommand"
                && orderId.ToString("N").Equals(activity.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)),
            "command handle span for TelPlaceOrderCommand");
        Assert.Equal(orderId.ToString("N"), handleSpan.GetTagItem(SemanticConventions.Commands.Tags.RouteKey));
    }

    [Fact]
    public async Task CommandHandleSpan_ShouldWriteTelemeterizedCommandPropertiesAsEdictTags()
    {
        var orderId = Guid.NewGuid();
        const string sku = "SKU-HANDLE-TELEM-1";
        using var capture = new SpanCapture();

        await fixture.Sender.SendAsync(new TelPlaceOrderCommand(orderId, sku));

        var handleSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Handle} TelPlaceOrderCommand"
                && orderId.ToString("N").Equals(activity.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)),
            "command handle span for TelPlaceOrderCommand");
        Assert.Equal(sku, handleSpan.GetTagItem("edict.sku"));
    }

    [Fact]
    public async Task PublishSpan_ShouldWriteTelemeterizedEventPropertiesAsEdictTags()
    {
        var orderId = Guid.NewGuid();
        const string sku = "SKU-EVTPUB-1";
        using var capture = new SpanCapture();

        await fixture.Sender.SendAsync(new TelPlaceOrderCommand(orderId, sku));

        var commandSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Command} TelPlaceOrderCommand"
                && orderId.ToString("N").Equals(activity.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)),
            "command span for TelPlaceOrderCommand");
        var publishSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Publish} TelOrderPlacedEvent"
                && activity.TraceId == commandSpan.TraceId,
            "publish span for TelOrderPlacedEvent");
        Assert.Equal(sku, publishSpan.GetTagItem("edict.sku"));
    }

    [Fact]
    public async Task HandleSpan_ShouldWriteTelemeterizedEventPropertiesAsEdictTags()
    {
        var orderId = Guid.NewGuid();
        const string sku = "SKU-EVTHANDLE-1";
        using var capture = new SpanCapture();

        await fixture.Sender.SendAsync(new TelPlaceOrderCommand(orderId, sku));

        var handleSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Handle} TelOrderPlacedEvent"
                && sku.Equals(activity.GetTagItem("edict.sku")),
            "event handle span for TelOrderPlacedEvent");
        Assert.Equal(sku, handleSpan.GetTagItem("edict.sku"));
    }

    [Fact]
    public async Task HandleSpan_ShouldBeNewRootLinkingToPublishSpan()
    {
        var orderId = Guid.NewGuid();
        const string sku = "SKU-EVTHANDLE-ROOT-1";
        using var capture = new SpanCapture();

        await fixture.Sender.SendAsync(new TelPlaceOrderCommand(orderId, sku));

        var publishSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Publish} TelOrderPlacedEvent"
                && sku.Equals(activity.GetTagItem("edict.sku")),
            "publish span for TelOrderPlacedEvent");
        var handleSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Handle} TelOrderPlacedEvent"
                && sku.Equals(activity.GetTagItem("edict.sku")),
            "event handle span for TelOrderPlacedEvent");

        // The consumer turn is its own trace — it does not share the producer's.
        Assert.Null(handleSpan.Parent);
        Assert.NotEqual(publishSpan.TraceId, handleSpan.TraceId);

        // ...but it links back to the publish span via the event's stamped context.
        var onlyLink = Assert.Single(handleSpan.Links);
        Assert.Equal(publishSpan.TraceId, onlyLink.Context.TraceId);
        Assert.Equal(publishSpan.SpanId, onlyLink.Context.SpanId);
    }

    [Fact]
    public async Task NoEdictSpanIsADetachedRoot_ExceptTheIntendedNewRoots()
    {
        var orderId = Guid.NewGuid();
        const string sku = "SKU-ORPHAN-SWEEP-1";
        using var capture = new SpanCapture();

        await fixture.Sender.SendAsync(new TelPlaceOrderCommand(orderId, sku));

        // The handle span is the last span in the flow to land, so waiting on it
        // guarantees the deferred consumer turn is captured before the sweep.
        await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Handle} TelOrderPlacedEvent"
                && sku.Equals(activity.GetTagItem("edict.sku")),
            "event handle span for TelOrderPlacedEvent");

        // The whole command -> handle flow carries the one telemeterized sku, so it
        // scopes the sweep to this turn's spans despite the shared cluster.
        var flowSpans = capture.Snapshot().Where(activity => sku.Equals(activity.GetTagItem("edict.sku"))).ToList();
        Assert.NotEmpty(flowSpans);

        // The only spans allowed to start their own trace are the originating Web
        // command and the per-turn consumer roots (handle / deduplicated). Every
        // other Edict span must nest under an explicit parent context.
        string[] allowedRootPrefixes =
        [
            SemanticConventions.Commands.Spans.Command,
            SemanticConventions.Events.Spans.Handle,
            SemanticConventions.Events.Spans.Deduplicated,
        ];
        var detachedRoots = flowSpans
            .Where(activity => activity.ParentSpanId == default)
            .Where(activity => !allowedRootPrefixes.Any(prefix => activity.OperationName.StartsWith($"{prefix} ")))
            .Select(activity => activity.OperationName)
            .ToList();
        Assert.Empty(detachedRoots);
    }

    [Fact]
    public async Task PublishSpan_ShouldBeParentChildUnderCommandSpan()
    {
        var orderId = Guid.NewGuid();
        using var capture = new SpanCapture();

        await fixture.Sender.SendAsync(new TelPlaceOrderCommand(orderId, "SKU-1"));

        var commandSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Commands.Spans.Command} TelPlaceOrderCommand"
                && orderId.ToString("N").Equals(activity.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)),
            "command span for TelPlaceOrderCommand");
        var publishSpan = await capture.WaitForSpanAsync(
            activity => activity.OperationName == $"{SemanticConventions.Events.Spans.Publish} TelOrderPlacedEvent",
            "publish span for TelOrderPlacedEvent");

        Assert.Equal(commandSpan.TraceId, publishSpan.TraceId);
        Assert.Equal(commandSpan.SpanId, publishSpan.ParentSpanId);
    }

    [Fact]
    public async Task PublishedEvent_ShouldBeStampedWithTraceContextFromPublishSpan()
    {
        var orderId = Guid.NewGuid();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = _ => { },
        };
        ActivitySource.AddActivityListener(listener);

        await fixture.Sender.SendAsync(new TelPlaceOrderCommand(orderId, "SKU-1"));

        var events = await WaitForEventsAsync(orderId);
        var edictEvent = Assert.Single(events);
        Assert.NotEqual(Guid.Empty, edictEvent.EventId);
        Assert.NotEqual(default, edictEvent.OccurredAt);
        Assert.NotNull(edictEvent.TraceId);
        Assert.NotNull(edictEvent.SpanId);
    }

    async Task<IReadOnlyList<EdictEvent>> WaitForEventsAsync(
        Guid orderId, int expectedCount = 1)
    {
        var captureGrain = fixture.Cluster.GrainFactory.GetGrain<ITelOrderEventCaptureGrain>(orderId);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var events = await captureGrain.GetCapturedEventsAsync();
            if (events.Count >= expectedCount)
            {
                return events;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        return await captureGrain.GetCapturedEventsAsync();
    }
}
