using System.Diagnostics;

using Edict.Telemetry;

using Orleans;

using Xunit;

namespace Edict.Tests.Conformance.EventHandler;

/// <summary>
/// Substrate-agnostic conformance that the deferred <c>edict.event.handle</c>
/// span nests under the originating <c>edict.event.publish</c> span across the
/// stream hop. The producer-side publish span (opened by the outbox executor)
/// and the consumer-side invocation span (opened by
/// <c>InvokeHandlerExecutor</c>) must share a trace and parent-child link.
/// </summary>
public abstract class EventHandlerSpanStitchAcrossOutboxHopScenarios<TFixture>
    where TFixture : ConformanceFixture
{
    readonly TFixture _fixture;

    protected EventHandlerSpanStitchAcrossOutboxHopScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DeferredInvocationSpan_ShouldNestUnderPublishSpan_AcrossStreamHop()
    {
        var customerId = Guid.NewGuid();
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => { lock (stopped) { stopped.Add(a); } },
        };
        ActivitySource.AddActivityListener(listener);

        await _fixture.Sender.Send(new NotifyCustomerCommand(customerId, "welcome"));

        var handler = _fixture.GrainFactory.GetGrain<IEmailHandlerProbe>(customerId);
        await EmailHandlerWaiters.WaitForHandledAsync(handler);

        // The probe's count increments inside Handle, but ActivityStopped
        // only fires after the executor's using-scope unwinds.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Activity publishSpan;
        Activity invocationSpan;
        lock (stopped)
        {
            publishSpan = stopped.First(a =>
                a.OperationName == "edict.event.publish CustomerNotifiedEvent");
            invocationSpan = stopped.First(a =>
                a.OperationName == "edict.event.handle CustomerNotifiedEvent"
                && a.ParentSpanId == publishSpan.SpanId);
        }

        Assert.Equal(publishSpan.TraceId, invocationSpan.TraceId);
    }
}
