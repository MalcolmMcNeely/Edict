using System.Diagnostics;

using Edict.Telemetry;
using Edict.Tests.Conformance.EventHandler;

using Orleans;

using Xunit;

namespace Edict.Tests.Conformance.Telemetry;

/// <summary>
/// Substrate-agnostic conformance that <c>[EdictTelemeterized]</c> on an event
/// property lands on both the producer-side <c>edict.event.publish</c> span
/// and the consumer-side <c>edict.event.handle</c> span. Uses
/// <see cref="CustomerNotifiedEvent.Reason"/> as the Telemeterized property —
/// the same event used for span-stitch tests, so the new wiring exercises an
/// already-substrate-routed event.
/// </summary>
public abstract class EventTelemeterizedTagsOnSpansScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected EventTelemeterizedTagsOnSpansScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PublishAndHandleSpans_ShouldCarryTelemeterizedEventTag()
    {
        var customerId = Guid.NewGuid();
        const string reason = "promo-trace-tag";
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => { lock (stopped) { stopped.Add(a); } },
        };
        ActivitySource.AddActivityListener(listener);

        await _fixture.Sender.SendAsync(new NotifyCustomerCommand(customerId, reason));

        var handler = _fixture.GrainFactory.GetGrain<IEmailHandlerProbe>(customerId);
        await EmailHandlerWaiters.WaitForHandledAsync(handler);

        // Activity disposal happens after Handle returns; small grace period
        // for ActivityStopped to deliver the final span.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Activity publishSpan;
        Activity handleSpan;
        lock (stopped)
        {
            publishSpan = stopped.First(a =>
                a.OperationName == $"{SemanticConventions.Events.Spans.Publish} CustomerNotifiedEvent"
                && reason.Equals(a.GetTagItem("edict.reason")));
            handleSpan = stopped.First(a =>
                a.OperationName == $"{SemanticConventions.Events.Spans.Handle} CustomerNotifiedEvent"
                && reason.Equals(a.GetTagItem("edict.reason")));
        }

        Assert.Equal(reason, publishSpan.GetTagItem("edict.reason"));
        Assert.Equal(reason, handleSpan.GetTagItem("edict.reason"));
    }
}
