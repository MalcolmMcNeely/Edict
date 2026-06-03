using Edict.Tests.Conformance.Idempotency;

using Xunit;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// The dedup ring is part of the framework-owned grain state, so it must survive
/// a grain deactivation: a same-<c>EventId</c> redelivery after reactivation is
/// suppressed (the ring reloaded from durable state), while a never-seen event
/// is handled. The reference stream is only the delivery vehicle; the asserted
/// property is the ring's durable survival.
/// </summary>
public abstract class RingSurvivesDeactivationScenarios<TFixture>
    where TFixture : PersistenceConformanceFixture
{
    readonly TFixture _fixture;

    protected RingSurvivesDeactivationScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DedupRing_ShouldSurviveGrainDeactivation()
    {
        var grainId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<IDedupPublisherGrain>(grainId);
        var consumer = _fixture.GrainFactory.GetGrain<IDedupTestConsumer>(grainId);

        var idX = Guid.NewGuid();
        var firstEvent = new DedupTestEvent(grainId, 1) with
        {
            EventId = idX,
            OccurredAt = DateTimeOffset.UtcNow,
        };
        await publisher.PublishAsync(firstEvent);
        await DedupTestWaiters.WaitForHandledCountAsync(consumer, expectedCount: 1);

        await consumer.DeactivateSelfAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));

        var idY = Guid.NewGuid();
        var secondEvent = new DedupTestEvent(grainId, 2) with
        {
            EventId = idY,
            OccurredAt = DateTimeOffset.UtcNow,
        };
        await publisher.PublishAsync(firstEvent);
        await publisher.PublishAsync(secondEvent);

        var reactivated = _fixture.GrainFactory.GetGrain<IDedupTestConsumer>(grainId);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ids = await reactivated.GetHandledEventIdsAsync();
            if (ids.Contains(idY))
            {
                break;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
        var handled = await reactivated.GetHandledEventIdsAsync();
        Assert.Contains(idY, handled);
        Assert.DoesNotContain(idX, handled);
    }
}
