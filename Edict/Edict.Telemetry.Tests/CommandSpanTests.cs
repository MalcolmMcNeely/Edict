using System.Diagnostics;

namespace Edict.Telemetry.Tests;

/// <summary>
/// Integration tests: the Command → Publish → Handle span tree (ADR 0003).
/// Asserts EdictDiagnostics lives in Edict.Telemetry and the span shape is intact.
/// </summary>
[Collection(TelemetryClusterCollection.Name)]
public sealed class CommandSpanTests(TelemetryClusterFixture fixture)
{
    [Fact]
    public async Task Send_opens_one_edict_span_per_command_dispatch()
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

        var span = stopped.Single(a => orderId.Equals(a.GetTagItem("edict.command.route_key")));
        Assert.Equal("edict.command TelPlaceOrderCommand", span.OperationName);
    }

    [Fact]
    public async Task Send_records_error_status_when_handler_throws()
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

        var span = stopped.Single(a => orderId.Equals(a.GetTagItem("edict.command.route_key")));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
    }

    [Fact]
    public async Task Send_writes_telemeterized_properties_as_edict_tags()
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

        var span = stopped.Single(a => orderId.Equals(a.GetTagItem("edict.command.route_key")));
        Assert.Equal(sku, span.GetTagItem("edict.telplaceordercommand.sku"));
    }

    [Fact]
    public async Task Publish_span_is_parent_child_under_command_span()
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
            a.OperationName == "edict.command TelPlaceOrderCommand"
            && orderId.Equals(a.GetTagItem("edict.command.route_key")));
        var publishSpan = stopped.Single(a =>
            a.OperationName == "edict.event.publish TelOrderPlacedEvent");

        Assert.Equal(commandSpan.TraceId, publishSpan.TraceId);
        Assert.Equal(commandSpan.SpanId, publishSpan.ParentSpanId);
    }

    [Fact]
    public async Task Published_event_is_stamped_with_trace_context_from_publish_span()
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
