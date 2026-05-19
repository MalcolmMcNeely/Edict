using Edict.Core.Tests.Grains;

namespace Edict.Core.Tests.Outbox;

// Block-intake, event arm (ADR 0019): once the DeadLetter slice hits the cap a
// redelivered event is NOT acked — the dedup ring slot is never committed, so
// Orleans keeps redelivering until an operator redrives. Nothing is silently
// dropped. In-memory cluster, no Azurite (ADR 0016).
[Collection(DeadLetterCapClusterCollection.Name)]
public sealed class DeadLetterCapEventTests(DeadLetterCapClusterFixture fixture)
{
    [Fact]
    public async Task RedeliveredEvent_ShouldNotBeAcked_WhenDeadLetterCapReached()
    {
        var id = Guid.NewGuid();
        var consumer = fixture.Cluster.GrainFactory.GetGrain<IBlockableConsumer>(id);
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IBlockIntakePublisher>(id);

        // Saturate the consumer's DeadLetter slice to the cap-of-1 limit.
        await consumer.SeedDeadLetterToCapAsync();

        var evt = new BlockIntakeEvent(id) { EventId = Guid.NewGuid() };
        await publisher.PublishAsync(evt);

        // Give the stream delivery (and any redelivery attempts) time to run.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // Not handled and — critically — the EventId was never committed to
        // the ring, so this is a genuine "will be redelivered", not a drop.
        Assert.Equal(0, await consumer.GetHandledCountAsync());
        Assert.False(await consumer.RingContainsAsync(evt.EventId));
    }
}
