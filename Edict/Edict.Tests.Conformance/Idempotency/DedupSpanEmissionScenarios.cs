using System.Diagnostics;

using Edict.Telemetry;

using Xunit;

namespace Edict.Tests.Conformance.Idempotency;

public abstract class DedupSpanEmissionScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected DedupSpanEmissionScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_ShouldEmitSpanTaggedDeduplicated_WhenRedeliveryIsSuppressed()
    {
        var grainId = Guid.NewGuid();
        var publisher = _fixture.GrainFactory.GetGrain<IDedupPublisherGrain>(grainId);
        var consumer = _fixture.GrainFactory.GetGrain<IDedupTestConsumer>(grainId);

        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);

        var sharedEventId = Guid.NewGuid();
        var evt = new DedupTestEvent(grainId, 1) with
        {
            EventId = sharedEventId,
            OccurredAt = DateTimeOffset.UtcNow,
        };

        await publisher.PublishAsync(evt);
        await DedupTestWaiters.WaitForHandledCountAsync(consumer, expectedCount: 1);

        await publisher.PublishAsync(evt);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        Activity? dedupSpan = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            dedupSpan = stopped.FirstOrDefault(
                a => a.OperationName == "edict.event.deduplicated DedupTestEvent");
            if (dedupSpan is not null)
            {
                break;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        Assert.NotNull(dedupSpan);
        Assert.Equal(true, dedupSpan!.GetTagItem("edict.deduplicated"));
    }
}
