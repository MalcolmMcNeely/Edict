using System.Diagnostics;

using Edict.Telemetry;

using Xunit;

namespace Edict.Tests.Conformance.Sagas;

/// <summary>
/// Substrate-agnostic conformance that guards the orphaned-command-span trap:
/// the traceparent must be captured while the handle span is still
/// <c>Activity.Current</c> inside <c>EdictSaga.DispatchEventAsync</c>, not later
/// in <c>CollectPendingOutboxEntries</c>.
/// </summary>
public abstract class SagaCommandSpanNestsUnderHandleSpanScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected SagaCommandSpanNestsUnderHandleSpanScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CommandSpan_ShouldNestUnderSagaHandleSpan_AcrossTheDispatchHop()
    {
        var workflowId = Guid.NewGuid();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => { lock (stopped) { stopped.Add(a); } },
        };
        ActivitySource.AddActivityListener(listener);

        var publisher = _fixture.GrainFactory.GetGrain<ISagaEventPublisher>(workflowId);
        await publisher.PublishAsync(new SagaTriggerEvent(workflowId) with
        {
            EventId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
        });

        var tracker = _fixture.GrainFactory.GetGrain<ISagaTrackerProbe>(workflowId);
        await SagaWaiters.WaitForReceivedAsync(tracker);

        // ActivityStopped fires after each executor's using-scope unwinds,
        // which can lag the visible tracker increment by a tick.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Activity handleSpan;
        Activity sendSpan;
        Activity commandSpan;
        lock (stopped)
        {
            handleSpan = stopped.Single(a => a.OperationName == "edict.event.handle SagaTriggerEvent");
            sendSpan = stopped.Single(a => a.OperationName == "edict.command.send SagaTrackerCommand");
            commandSpan = stopped.Single(a => a.OperationName == "edict.command SagaTrackerCommand");
        }

        Assert.Equal(handleSpan.SpanId, sendSpan.ParentSpanId);
        Assert.Equal(handleSpan.TraceId, sendSpan.TraceId);
        Assert.Equal(handleSpan.TraceId, commandSpan.TraceId);
    }
}
