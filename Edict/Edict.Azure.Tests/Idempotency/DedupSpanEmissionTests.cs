using System.Diagnostics;

using Edict.Telemetry;

namespace Edict.Azure.Tests.Idempotency;

/// <summary>
/// A suppressed redelivery must surface as a span tagged
/// <c>edict.deduplicated=true</c> so operators can see at-least-once
/// duplicates without trawling logs. Proves the suppression branch of
/// <see cref="Core.Idempotency.EdictIdempotencyBase{TPayload}.OnStreamEventAsync"/>
/// emits the diagnostic span when the event arrives via the real Azure Queue
/// stream provider (parent trace context is rehydrated from the wire payload).
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class DedupSpanEmissionTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task HandleAsync_ShouldEmitSpanTaggedDeduplicated_WhenRedeliveryIsSuppressed()
    {
        var grainId = Guid.NewGuid();
        var publisher = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupPublisherGrain>(grainId);
        var consumer = fixture.Cluster.GrainFactory.GetGrain<IAzureDedupTestConsumer>(grainId);

        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var sharedEventId = Guid.NewGuid();
        var evt = new AzureDedupTestEvent(grainId, 1) with
        {
            EventId = sharedEventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(evt);
        await WaitForHandledCountAsync(consumer, expectedCount: 1);

        // Republish the same event id; the consumer must drop it and emit the
        // dedup span.
        await publisher.PublishAsync(evt);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        Activity? dedupSpan = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            dedupSpan = stopped.FirstOrDefault(
                a => a.OperationName == "edict.event.deduplicated AzureDedupTestEvent");
            if (dedupSpan is not null)
            {
                break;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        Assert.NotNull(dedupSpan);
        Assert.Equal(true, dedupSpan!.GetTagItem("edict.deduplicated"));
    }

    static async Task<IReadOnlyList<Guid>> WaitForHandledCountAsync(
        IAzureDedupTestConsumer consumer,
        int expectedCount,
        int timeoutSeconds = 20)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ids = await consumer.GetHandledEventIdsAsync();
            if (ids.Count >= expectedCount)
            {
                return ids;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await consumer.GetHandledEventIdsAsync();
    }
}
