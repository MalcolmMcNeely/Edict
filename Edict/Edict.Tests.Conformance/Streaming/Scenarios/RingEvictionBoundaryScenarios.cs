using Edict.Tests.Conformance.Idempotency;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis conformance for the dedup ring's eviction boundary: once the ring
/// is full, the oldest <c>EventId</c> is evicted to make room, so a later redelivery
/// of that evicted id is <em>re-handled</em> rather than silently suppressed. Publishes
/// one more distinct id than the ring holds, then redelivers the evicted-oldest and
/// proves the consumer ran it again. The complement of
/// <see cref="EventHandlerDedupsWithinRingScenarios{TFixture}"/> (suppression of an id
/// still inside the window): this asserts the ring wrapped correctly at the boundary
/// instead of leaving a stale id pinned and suppressing a genuine redelivery.
/// </summary>
public abstract class RingEvictionBoundaryScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected RingEvictionBoundaryScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EvictedOldestEventId_IsReHandled_WhenRedeliveredAfterTheRingWraps()
    {
        // DedupTestConsumer holds a three-slot ring; capacity + 1 distinct ids force
        // the oldest out of the window when the fourth commits.
        const int capacity = 3;
        var grainId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<IDedupPublisherGrain>(grainId);
        var consumer = _fixture.GrainFactory.GetGrain<IDedupTestConsumer>(grainId);

        var ids = Enumerable.Range(0, capacity + 1).Select(_ => Guid.NewGuid()).ToArray();

        // Arrange — publish capacity + 1 distinct ids in order so the first committed
        // (ids[0]) is the one evicted when the ring fills. Awaiting each before the next
        // pins the eviction order; the streaming provider delivers them serially anyway.
        for (var sequence = 0; sequence < ids.Length; sequence++)
        {
            await publisher.PublishAsync(new DedupTestEvent(grainId, sequence) with
            {
                EventId = ids[sequence],
                OccurredAt = DateTimeOffset.UtcNow,
            });
        }
        await DedupTestWaiters.WaitForHandledCountAsync(consumer, expectedCount: ids.Length);

        // Act — redeliver the evicted-oldest id. A ring that wrapped correctly no longer
        // remembers it, so the consumer must dispatch it a second time; a ring that left
        // it pinned would suppress this and the count would stall at capacity + 1.
        await publisher.PublishAsync(new DedupTestEvent(grainId, 0) with
        {
            EventId = ids[0],
            OccurredAt = DateTimeOffset.UtcNow,
        });
        var handled = await DedupTestWaiters.WaitForHandledCountAsync(consumer, expectedCount: ids.Length + 1);

        // Assert — every distinct id ran once and the evicted-oldest ran a second time.
        Assert.Equal(ids.Length + 1, handled.Count);
        Assert.Equal(2, handled.Count(handledId => handledId == ids[0]));
    }
}
