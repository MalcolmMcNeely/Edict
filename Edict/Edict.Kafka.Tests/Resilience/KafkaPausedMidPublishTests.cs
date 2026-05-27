namespace Edict.Kafka.Tests.Resilience;

// Tracer for slice 7: prove the contract-floor invariant
// (acks=all + enable.idempotence=true + enable.auto.commit=false) survives a
// transient broker disruption. Publisher pushes events for one aggregate
// sequentially through a pause window — each await blocks inside librdkafka
// during the pause and resumes once the broker is back. Consumer must
// observe every event exactly once and in per-aggregate sequence order.
//
// Sequential await on the producer side is deliberate: it removes any
// ambiguity around fire-and-forget grain-call ordering and isolates what the
// test is pinning to the consumer's poll-through-disruption behaviour and the
// producer's reconnect-and-finish-publish behaviour.
//
// Multi-broker consumer-group rebalance is a #139b multi-silo concern and is
// captured separately in ADR-0028.
[Collection(KafkaResilienceCollection.Name)]
public sealed class KafkaPausedMidPublishTests(KafkaResilienceClusterFixture fixture)
{
    [Fact]
    public async Task ConsumerHandle_ShouldDeliverAllEventsInOrder_WhenKafkaPausedMidPublishThenResumed()
    {
        await fixture.EnsureRunningAsync();

        var aggregateId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory
            .GetGrain<IKafkaResilienceEventPublisher>(aggregateId);
        var consumer = fixture.Cluster.GrainFactory
            .GetGrain<IKafkaResilienceTestConsumer>(aggregateId);

        for (var i = 1; i <= 3; i++)
        {
            await publisher.PublishEventAsync(NewEvent(aggregateId, i));
        }

        await fixture.PauseKafkaAsync();

        // Schedule the unpause on a background task so the sequential
        // publishes below can block inside the producer; after the delay,
        // librdkafka reconnects and the pending ProduceAsync calls resolve.
        var unpauseTask = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            await fixture.UnpauseKafkaAsync();
        });

        for (var i = 4; i <= 6; i++)
        {
            await publisher.PublishEventAsync(NewEvent(aggregateId, i));
        }

        await unpauseTask;

        var handled = await KafkaResilienceWaiters.WaitForHandledAsync(consumer, expectedCount: 6);

        Assert.Equal(6, handled.Count);
        Assert.Equal(Enumerable.Range(1, 6).ToArray(), handled.ToArray());
    }

    static KafkaResilienceTestEvent NewEvent(Guid aggregateId, int sequence) =>
        new KafkaResilienceTestEvent(aggregateId, sequence) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        };
}
