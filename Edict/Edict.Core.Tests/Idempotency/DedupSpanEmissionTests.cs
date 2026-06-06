using System.Diagnostics;

using Edict.Core.Tests.TestSupport;
using Edict.Telemetry;

using Xunit;

namespace Edict.Core.Tests.Idempotency;

[Collection(SubstrateIndependentCollection.Name)]
public sealed class DedupSpanEmissionTests
{
    readonly SubstrateIndependentClusterFixture _fixture;

    public DedupSpanEmissionTests(SubstrateIndependentClusterFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_ShouldEmitSpanTaggedDeduplicated_WhenRedeliveryIsSuppressed()
    {
        var grainId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<IDedupSpanPublisher>(grainId);
        var consumer = _fixture.GrainFactory.GetGrain<IDedupSpanConsumer>(grainId);

        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var sharedEventId = Guid.NewGuid();
        // The publish span's identity rides on the wire event; the dedup span links
        // back to it, so stamp it as PublishEventExecutor would in production.
        var publishTraceId = ActivityTraceId.CreateRandom().ToHexString();
        var publishSpanId = ActivitySpanId.CreateRandom().ToHexString();
        var edictEvent = new DedupSpanTestEvent(grainId, 1) with
        {
            EventId = sharedEventId,
            OccurredAt = DateTimeOffset.UtcNow,
            TraceId = publishTraceId,
            SpanId = publishSpanId,
        };

        await publisher.PublishAsync(edictEvent);
        await DedupSpanWaiters.WaitForHandledCountAsync(consumer, expectedCount: 1);

        await publisher.PublishAsync(edictEvent);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        Activity? dedupSpan = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            dedupSpan = stopped.FirstOrDefault(
                a => a.OperationName == $"{SemanticConventions.Events.Spans.Deduplicated} DedupSpanTestEvent");
            if (dedupSpan is not null)
            {
                break;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        Assert.NotNull(dedupSpan);
        Assert.Equal(true, dedupSpan!.GetTagItem(SemanticConventions.Events.Tags.Deduplicated));

        // A suppressed redelivery is its own consumer turn: a new trace root that
        // links back to the publish span rather than nesting under it.
        Assert.Null(dedupSpan.Parent);
        var link = Assert.Single(dedupSpan.Links);
        Assert.Equal(publishTraceId, link.Context.TraceId.ToHexString());
        Assert.Equal(publishSpanId, link.Context.SpanId.ToHexString());
    }
}
