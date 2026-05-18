using System.Diagnostics;

using Edict.Contracts.Events;
using Edict.Core.Tests.Grains;
using Edict.Telemetry;

namespace Edict.Core.Tests.Events;

[Collection(EdictClusterCollection.Name)]
public sealed class EventPublishingTests(EdictClusterFixture fixture)
{
    // Cycle 1 — tracer bullet: accepted command → event on domain stream
    [Fact]
    public async Task Accepted_command_publishes_event_to_domain_stream()
    {
        var orderId = Guid.NewGuid();

        await fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-1"));

        var events = await WaitForEventsAsync(orderId);
        var placed = Assert.IsType<OrderPlacedEvent>(Assert.Single(events));
        Assert.Equal(orderId, placed.OrderId);
        Assert.Equal("SKU-1", placed.Sku);
    }

    // Cycle 2 — rejected command discards buffer; nothing reaches the stream
    [Fact]
    public async Task Rejected_command_publishes_no_events()
    {
        var orderId = Guid.NewGuid();

        await fixture.Sender.Send(new CancelOrderCommand(orderId, "changed mind"));

        await Task.Delay(TimeSpan.FromSeconds(2));
        var captureGrain = fixture.Cluster.GrainFactory.GetGrain<IOrderEventCaptureGrain>(orderId);
        Assert.Empty(await captureGrain.GetCapturedEventsAsync());
    }

    // Cycle 3 — flushed event is stamped with EventId, OccurredAt, and trace context
    [Fact]
    public async Task Published_event_is_stamped_with_EventId_OccurredAt_and_trace_context()
    {
        var orderId = Guid.NewGuid();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = _ => { },
        };
        ActivitySource.AddActivityListener(listener);

        await fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-1"));

        var events = await WaitForEventsAsync(orderId);
        var evt = Assert.Single(events);
        Assert.NotEqual(Guid.Empty, evt.EventId);
        Assert.NotEqual(default, evt.OccurredAt);
        Assert.NotNull(evt.TraceId);
        Assert.NotNull(evt.SpanId);
    }

    // Cycle 4 — publish span is a child of the command span (ADR 0003)
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

        await fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-1"));

        var commandSpan = stopped.Single(a =>
            a.OperationName == "edict.command PlaceOrderCommand"
            && orderId.Equals(a.GetTagItem("edict.command.route_key")));
        var publishSpan = stopped.Single(a =>
            a.OperationName == "edict.event.publish OrderPlacedEvent");

        Assert.Equal(commandSpan.TraceId, publishSpan.TraceId);
        Assert.Equal(commandSpan.SpanId, publishSpan.ParentSpanId);
    }

    private async Task<IReadOnlyList<EdictEvent>> WaitForEventsAsync(Guid orderId, int expectedCount = 1)
    {
        var captureGrain = fixture.Cluster.GrainFactory.GetGrain<IOrderEventCaptureGrain>(orderId);
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
