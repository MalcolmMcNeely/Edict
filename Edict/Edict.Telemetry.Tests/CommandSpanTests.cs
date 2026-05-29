using System.Diagnostics;

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
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await fixture.Sender.Send(new TelPlaceOrderCommand(orderId, "SKU-1"));

        var span = stopped.Single(a => orderId.Equals(a.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)));
        Assert.Equal($"{SemanticConventions.Commands.Spans.Command} TelPlaceOrderCommand", span.OperationName);
    }

    [Fact]
    public async Task Send_ShouldRecordErrorStatus_WhenHandlerThrows()
    {
        var orderId = Guid.NewGuid();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await Assert.ThrowsAnyAsync<Exception>(
            () => fixture.Sender.Send(new TelFailOrderCommand(orderId)));

        var span = stopped.Single(a => orderId.Equals(a.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public async Task Send_ShouldWriteTelemeterizedPropertiesAsEdictTags()
    {
        var orderId = Guid.NewGuid();
        const string sku = "SKU-TELEM-1";
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await fixture.Sender.Send(new TelPlaceOrderCommand(orderId, sku));

        var span = stopped.Single(a => orderId.Equals(a.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)));
        Assert.Equal(sku, span.GetTagItem("edict.sku"));
    }

    [Fact]
    public async Task PublishSpan_ShouldWriteTelemeterizedEventPropertiesAsEdictTags()
    {
        var orderId = Guid.NewGuid();
        const string sku = "SKU-EVTPUB-1";
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await fixture.Sender.Send(new TelPlaceOrderCommand(orderId, sku));

        var publishSpan = stopped.Single(a =>
            a.OperationName == $"{SemanticConventions.Events.Spans.Publish} TelOrderPlacedEvent");
        Assert.Equal(sku, publishSpan.GetTagItem("edict.sku"));
    }

    [Fact]
    public async Task HandleSpan_ShouldWriteTelemeterizedEventPropertiesAsEdictTags()
    {
        var orderId = Guid.NewGuid();
        const string sku = "SKU-EVTHANDLE-1";
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await fixture.Sender.Send(new TelPlaceOrderCommand(orderId, sku));
        await WaitForEventsAsync(orderId);

        var handleSpan = stopped.SingleOrDefault(a =>
            a.OperationName == $"{SemanticConventions.Events.Spans.Handle} TelOrderPlacedEvent");
        Assert.NotNull(handleSpan);
        Assert.Equal(sku, handleSpan!.GetTagItem("edict.sku"));
    }

    [Fact]
    public async Task PublishSpan_ShouldBeParentChildUnderCommandSpan()
    {
        var orderId = Guid.NewGuid();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await fixture.Sender.Send(new TelPlaceOrderCommand(orderId, "SKU-1"));

        var commandSpan = stopped.Single(a =>
            a.OperationName == $"{SemanticConventions.Commands.Spans.Command} TelPlaceOrderCommand"
            && orderId.Equals(a.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)));
        var publishSpan = stopped.Single(a =>
            a.OperationName == $"{SemanticConventions.Events.Spans.Publish} TelOrderPlacedEvent");

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

        await fixture.Sender.Send(new TelPlaceOrderCommand(orderId, "SKU-1"));

        var events = await WaitForEventsAsync(orderId);
        var evt = Assert.Single(events);
        Assert.NotEqual(Guid.Empty, evt.EventId);
        Assert.NotEqual(default, evt.OccurredAt);
        Assert.NotNull(evt.TraceId);
        Assert.NotNull(evt.SpanId);
    }

    private async Task<IReadOnlyList<Edict.Contracts.Events.EdictEvent>> WaitForEventsAsync(
        Guid orderId, int expectedCount = 1)
    {
        var captureGrain = fixture.Cluster.GrainFactory.GetGrain<ITelOrderEventCaptureGrain>(orderId);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var events = await captureGrain.GetCapturedEventsAsync();
            if (events.Count >= expectedCount)
                return events;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        return await captureGrain.GetCapturedEventsAsync();
    }
}
