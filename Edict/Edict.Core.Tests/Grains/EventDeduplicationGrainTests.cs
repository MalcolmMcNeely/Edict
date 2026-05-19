using System.Diagnostics;
using Edict.Telemetry;

namespace Edict.Core.Tests.Grains;

[Collection(EdictClusterCollection.Name)]
public sealed class EventDeduplicationGrainTests(EdictClusterFixture fixture)
{
    // Cycle 1 — tracer bullet: event delivered to dedup grain is dispatched
    [Fact]
    public async Task Event_delivered_to_dedup_grain_is_dispatched()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IDedupPublisherGrain>(grainId);
        var grain = fixture.Cluster.GrainFactory.GetGrain<IDedupTestGrain>(grainId);

        var evt = new DedupTestEvent(grainId, 1) with { EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow };
        await publisher.PublishAsync(evt);

        var handled = await WaitForHandledAsync(grain);
        Assert.Single(handled);
        Assert.Equal(evt.EventId, handled[0]);
    }

    // Cycle 2 — duplicate EventId is suppressed
    [Fact]
    public async Task Duplicate_EventId_is_suppressed()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IDedupPublisherGrain>(grainId);
        var grain = fixture.Cluster.GrainFactory.GetGrain<IDedupTestGrain>(grainId);

        var sharedEventId = Guid.NewGuid();
        var first = new DedupTestEvent(grainId, 1) with { EventId = sharedEventId, OccurredAt = DateTimeOffset.UtcNow };
        var duplicate = new DedupTestEvent(grainId, 2) with { EventId = sharedEventId, OccurredAt = DateTimeOffset.UtcNow };

        await publisher.PublishAsync(first);
        await WaitForHandledAsync(grain, expectedCount: 1);

        await publisher.PublishAsync(duplicate);
        await Task.Delay(TimeSpan.FromSeconds(2));

        var handled = await grain.GetHandledEventIdsAsync();
        Assert.Single(handled);
    }

    // Cycle 3 — EventId committed only after dispatch succeeds;
    //           simulated redelivery (same EventId) is accepted when dispatch previously threw
    [Fact]
    public async Task EventId_committed_only_after_dispatch_succeeds()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IDedupPublisherGrain>(grainId);
        var grain = fixture.Cluster.GrainFactory.GetGrain<IDedupTestGrain>(grainId);

        var eventId = Guid.NewGuid();
        var evt = new DedupTestEvent(grainId, 1) with { EventId = eventId, OccurredAt = DateTimeOffset.UtcNow };

        // First delivery: dispatch throws → EventId not committed
        await grain.ArmThrowOnNextAsync();
        await publisher.PublishAsync(evt);
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Empty(await grain.GetHandledEventIdsAsync());

        // Simulated redelivery: same EventId, dispatch succeeds → committed
        await publisher.PublishAsync(evt);
        var handled = await WaitForHandledAsync(grain);
        Assert.Single(handled);
        Assert.Equal(eventId, handled[0]);

        // Third delivery of same EventId: now in ring → suppressed
        await publisher.PublishAsync(evt);
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Single(await grain.GetHandledEventIdsAsync());
    }

    // Cycle 4 — unhandled event type consumes no ring slot
    [Fact]
    public async Task Unhandled_event_type_consumes_no_ring_slot()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IDedupPublisherGrain>(grainId);
        var grain = fixture.Cluster.GrainFactory.GetGrain<IDedupTestGrain>(grainId);

        // Fill ring (size 3) with handled events
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        await publisher.PublishAsync(new DedupTestEvent(grainId, 1) with { EventId = id1, OccurredAt = DateTimeOffset.UtcNow });
        await publisher.PublishAsync(new DedupTestEvent(grainId, 2) with { EventId = id2, OccurredAt = DateTimeOffset.UtcNow });
        await publisher.PublishAsync(new DedupTestEvent(grainId, 3) with { EventId = id3, OccurredAt = DateTimeOffset.UtcNow });
        await WaitForHandledAsync(grain, expectedCount: 3);

        // Publish an event type the grain does not handle (goes to DedupTest stream but DispatchAsync returns false)
        var unhandledEvt = new OrderPlacedEvent(grainId, "SKU-UNHANDLED") with { EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow };
        await publisher.PublishAsync(unhandledEvt);
        await Task.Delay(TimeSpan.FromSeconds(2));

        // If unhandled consumed a ring slot, id1 would be evicted and a resend would dispatch it again.
        // Correct behavior: unhandled consumed no slot, id1 is still in ring and is suppressed.
        await publisher.PublishAsync(new DedupTestEvent(grainId, 1) with { EventId = id1, OccurredAt = DateTimeOffset.UtcNow });
        await Task.Delay(TimeSpan.FromSeconds(2));

        var handled = await grain.GetHandledEventIdsAsync();
        Assert.Equal(3, handled.Count);
    }

    // Cycle 5 — dedup-suppressed redelivery emits span tagged edict.deduplicated=true
    [Fact]
    public async Task Suppressed_redelivery_emits_span_tagged_deduplicated()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IDedupPublisherGrain>(grainId);
        var grain = fixture.Cluster.GrainFactory.GetGrain<IDedupTestGrain>(grainId);

        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var sharedEventId = Guid.NewGuid();
        var evt = new DedupTestEvent(grainId, 1) with { EventId = sharedEventId, OccurredAt = DateTimeOffset.UtcNow };

        await publisher.PublishAsync(evt);
        await WaitForHandledAsync(grain);

        await publisher.PublishAsync(evt);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (stopped.Any(a => a.OperationName.StartsWith("edict.event.deduplicated")))
                break;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        var dedupSpan = stopped.First(a => a.OperationName == "edict.event.deduplicated DedupTestEvent");
        Assert.Equal(true, dedupSpan.GetTagItem("edict.deduplicated"));
    }

    // Cycle 6 — ring survives grain deactivation (persisted state)
    [Fact]
    public async Task Ring_survives_grain_deactivation()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IDedupPublisherGrain>(grainId);
        var grain = fixture.Cluster.GrainFactory.GetGrain<IDedupTestGrain>(grainId);

        // Deliver first event, wait for it to be handled and state persisted
        var idX = Guid.NewGuid();
        var evtX = new DedupTestEvent(grainId, 1) with { EventId = idX, OccurredAt = DateTimeOffset.UtcNow };
        await publisher.PublishAsync(evtX);
        await WaitForHandledAsync(grain);

        // Deactivate; the ring (containing idX) is in persisted state
        await grain.DeactivateSelfAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Publish evtX again (simulated redelivery) — ring should suppress it after reactivation
        // Publish evtY (new event) — ring should accept it (proves grain reactivated and is working)
        var idY = Guid.NewGuid();
        var evtY = new DedupTestEvent(grainId, 2) with { EventId = idY, OccurredAt = DateTimeOffset.UtcNow };
        await publisher.PublishAsync(evtX);
        await publisher.PublishAsync(evtY);

        // Wait for evtY to be dispatched (confirms grain is alive and processing)
        var reactivatedGrain = fixture.Cluster.GrainFactory.GetGrain<IDedupTestGrain>(grainId);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ids = await reactivatedGrain.GetHandledEventIdsAsync();
            if (ids.Contains(idY))
                break;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
        var handled = await reactivatedGrain.GetHandledEventIdsAsync();
        Assert.Contains(idY, handled);
        Assert.DoesNotContain(idX, handled); // ring suppressed the redelivery
    }

    private async Task<IReadOnlyList<Guid>> WaitForHandledAsync(IDedupTestGrain grain, int expectedCount = 1)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ids = await grain.GetHandledEventIdsAsync();
            if (ids.Count >= expectedCount)
                return ids;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        return await grain.GetHandledEventIdsAsync();
    }
}
