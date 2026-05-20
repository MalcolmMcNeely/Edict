using System.Diagnostics;

using Edict.Contracts.Events;
using Edict.Core.Tests.Grains;
using Edict.Telemetry;

namespace Edict.Core.Tests.EventHandler;

/// <summary>
/// End-to-end stream-callback proof for <c>EdictEventHandler</c> (ADR 0023):
/// publishing a handled event lands one invocation of the consumer's
/// <c>Handle</c>, but the path goes through the InvokeHandler Outbox effect —
/// not inline on the stream-callback hot path. Publishing an unhandled event
/// type stays a pure no-op (no ring slot consumed). Both behaviours are
/// asserted observably (handler count) rather than against any private
/// implementation state.
/// </summary>
[Collection(EdictClusterCollection.Name)]
public sealed class EdictEventHandlerStreamCallbackTests(EdictClusterFixture fixture)
{
    [Fact]
    public async Task OnStreamEvent_ShouldRunHandleExactlyOnce_WhenHandledTypePublished()
    {
        var orderId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IProjectionPublisherGrain>(orderId);
        var handler = fixture.Cluster.GrainFactory.GetGrain<IOrderEmailHandlerProbe>(orderId);

        var evt = new OrderPlacedEvent(orderId, "SKU-EVT") with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishToStreamAsync("Orders", evt);

        await WaitForHandledAsync(handler, expected: 1);
        Assert.Equal(1, await handler.GetHandledCountAsync());
    }

    [Fact]
    public async Task OnStreamEvent_ShouldSuppressReStage_WhenSameEventIdRedelivered()
    {
        var orderId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IProjectionPublisherGrain>(orderId);
        var handler = fixture.Cluster.GrainFactory.GetGrain<IOrderEmailHandlerProbe>(orderId);

        // Same EventId published twice — the dedup ring committed by the first
        // delivery must suppress the second so Handle runs exactly once,
        // proving ADR-0023's at-most-once *staging* layered on ADR-0002's
        // at-least-once delivery. Re-stage of the InvokeHandler entry would
        // run Handle twice.
        var eventId = Guid.NewGuid();
        var evt = new OrderPlacedEvent(orderId, "SKU-DEDUP") with
        {
            EventId = eventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishToStreamAsync("Orders", evt);
        await WaitForHandledAsync(handler, expected: 1);

        // Redeliver the same event — same EventId.
        await publisher.PublishToStreamAsync("Orders", evt);
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.Equal(1, await handler.GetHandledCountAsync());
    }

    [Fact]
    public async Task OnStreamEvent_ShouldBeNoOp_WhenEventTypeIsUnhandled()
    {
        var aggregateId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IProjectionPublisherGrain>(aggregateId);
        var handler = fixture.Cluster.GrainFactory.GetGrain<IOrderEmailHandlerProbe>(aggregateId);

        // DedupTestEvent has no Handle on TestOrderEmailHandler → HandlesType
        // returns false → no ring slot consumed, no InvokeHandler entry staged.
        var unhandled = new DedupTestEvent(aggregateId, 1) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        // DedupTestEvent's stream is "DedupTest" — publish a HANDLED stream
        // instead but with an unhandled event-type carried over. Easiest:
        // publish DedupTestEvent onto the "Orders" stream so the handler's
        // implicit subscription delivers it; the type-check is the gate
        // under test.
        await publisher.PublishToStreamAsync("Orders", unhandled);

        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await handler.GetHandledCountAsync());
    }

    [Fact]
    public async Task DeferredInvocationSpan_ShouldNestUnderPublishSpan_AcrossOutboxHop()
    {
        var orderId = Guid.NewGuid();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => { lock (stopped) { stopped.Add(a); } },
        };
        ActivitySource.AddActivityListener(listener);

        await fixture.Sender.Send(new PlaceOrderCommand(orderId, "SKU-TRC"));

        var handler = fixture.Cluster.GrainFactory.GetGrain<IOrderEmailHandlerProbe>(orderId);
        await WaitForHandledAsync(handler, expected: 1);

        // The publish span and the deferred-invocation span share the trace,
        // and the invocation hangs off the publish span — proving the
        // InvokeHandler executor restored the captured traceparent rather
        // than starting a fresh trace when the engine drained it (ADR 0003).
        Activity publishSpan;
        Activity invocationSpan;
        lock (stopped)
        {
            publishSpan = stopped.First(a => a.OperationName == "edict.event.publish OrderPlacedEvent");
            invocationSpan = stopped.First(a =>
                a.OperationName == "edict.event.handle OrderPlacedEvent"
                && a.ParentSpanId == publishSpan.SpanId);
        }

        Assert.Equal(publishSpan.TraceId, invocationSpan.TraceId);
    }

    static async Task WaitForHandledAsync(IOrderEmailHandlerProbe handler, int expected)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await handler.GetHandledCountAsync() >= expected)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }
}
