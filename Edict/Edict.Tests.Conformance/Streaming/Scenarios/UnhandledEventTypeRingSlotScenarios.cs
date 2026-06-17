using Edict.Tests.Conformance.Idempotency;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis conformance that an unhandled event type delivered on the
/// stream consumes no dedup-ring slot: it must not evict a still-relevant
/// <c>EventId</c>, so a later redelivery of that id is still suppressed.
/// </summary>
public abstract class UnhandledEventTypeRingSlotScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected UnhandledEventTypeRingSlotScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_ShouldConsumeNoRingSlot_WhenEventTypeIsUnhandled()
    {
        var grainId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<IDedupPublisherGrain>(grainId);
        var consumer = _fixture.GrainFactory.GetGrain<IDedupTestConsumer>(grainId);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        await publisher.PublishAsync(new DedupTestEvent(grainId, 1) with
        {
            EventId = id1,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await publisher.PublishAsync(new DedupTestEvent(grainId, 2) with
        {
            EventId = id2,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await publisher.PublishAsync(new DedupTestEvent(grainId, 3) with
        {
            EventId = id3,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await DedupTestWaiters.WaitForHandledCountAsync(consumer, expectedCount: 3);

        var unhandled = new UnhandledDedupTestEvent(grainId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };
        await publisher.PublishAsync(unhandled);

        // If the unhandled event had evicted id1 from the ring it would
        // dispatch again — correct behaviour is still suppressed.
        await publisher.PublishAsync(new DedupTestEvent(grainId, 1) with
        {
            EventId = id1,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        // Sentinel-after: a fresh handled event published last on the same serially-
        // delivered stream. Gate on the sentinel id landing, not a handled count — a
        // count gate would be satisfied by the leaked id1 redelivery itself and return
        // before the sentinel, masking the leak. Once the sentinel is handled, both the
        // unhandled event and the id1 redelivery ahead of it have been processed, so the
        // handled set is exactly {id1, id2, id3, id4} with id1 appearing once. A
        // redelivery that leaked through the ring would make it five with id1 twice.
        var id4 = Guid.NewGuid();
        await publisher.PublishAsync(new DedupTestEvent(grainId, 4) with
        {
            EventId = id4,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        var handled = await DedupTestWaiters.WaitForHandledIdAsync(consumer, id4);

        Assert.Equal(4, handled.Count);
        Assert.Equal(1, handled.Count(id => id == id1));
    }
}
