using Xunit;

namespace Edict.Tests.Conformance.Idempotency;

public abstract class UnhandledEventTypeRingSlotScenarios<TFixture>
    where TFixture : ConformanceFixture
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
        await Task.Delay(TimeSpan.FromSeconds(3));

        // If the unhandled event had evicted id1 from the ring it would
        // dispatch again — correct behaviour is still suppressed.
        await publisher.PublishAsync(new DedupTestEvent(grainId, 1) with
        {
            EventId = id1,
            OccurredAt = DateTimeOffset.UtcNow,
        });
        await Task.Delay(TimeSpan.FromSeconds(3));

        var handled = await consumer.GetHandledEventIdsAsync();
        Assert.Equal(3, handled.Count);
    }
}
