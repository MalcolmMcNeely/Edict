using System.Diagnostics;

using Edict.Telemetry;
using Edict.Core.Tests.Grains;

namespace Edict.Core.Tests.Grains;

[Collection(EdictClusterCollection.Name)]
public sealed class ProjectionBuilderTests(EdictClusterFixture fixture)
{
    // Cycle 3 — tracer bullet: command acceptance delivers event to projection grain
    [Fact]
    public async Task HandleAsync_ShouldDeliverEventToProjectionGrain_WhenCommandIsAccepted()
    {
        var orderId = Guid.NewGuid();

        await fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-1"));

        var projection = fixture.Cluster.GrainFactory.GetGrain<IOrderProjectionAccess>(orderId);
        await WaitForProjectionAsync(projection, expectedCount: 1);
        Assert.Equal(1, await projection.GetOrderCountAsync());
    }

    // Cycle 4 — handler span is a child of the publish span (ADR 0003)
    [Fact]
    public async Task HandlerSpan_ShouldBeChildOfPublishSpan()
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

        var projection = fixture.Cluster.GrainFactory.GetGrain<IOrderProjectionAccess>(orderId);
        await WaitForProjectionAsync(projection, expectedCount: 1);

        var publishSpan = stopped.Single(a => a.OperationName == "edict.event.publish OrderPlacedEvent"
            && a.TraceId != default);
        // Multiple grain types may handle the same event; verify at least one handler is a
        // direct child of the publish span (the in-memory ProjectionBuilderGrain included).
        var handlerSpan = stopped
            .Where(a => a.OperationName == "edict.event.handle OrderPlacedEvent"
                && a.ParentSpanId == publishSpan.SpanId)
            .First();

        Assert.Equal(publishSpan.TraceId, handlerSpan.TraceId);
    }

    // Cycle 5 — event type with no Handle overload is a no-op; projection count unchanged
    [Fact]
    public async Task HandleAsync_ShouldBeNoOp_WhenEventTypeIsUnhandled()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IProjectionPublisherGrain>(grainId);
        var projection = fixture.Cluster.GrainFactory.GetGrain<IOrderProjectionAccess>(grainId);

        // DedupTestEvent has no Handle in OrderProjectionBuilder → DispatchAsync returns false
        var unhandled = new DedupTestEvent(grainId, 1) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };
        await publisher.PublishToStreamAsync("Orders", unhandled);

        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await projection.GetOrderCountAsync());
    }

    private static async Task WaitForProjectionAsync(IOrderProjectionAccess projection, int expectedCount)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await projection.GetOrderCountAsync() >= expectedCount)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
