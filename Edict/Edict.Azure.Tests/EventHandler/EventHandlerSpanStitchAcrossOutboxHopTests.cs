using System.Diagnostics;

using Edict.Telemetry;

namespace Edict.Azure.Tests.EventHandler;

/// <summary>
/// ADR 0003 trace-context stitch end-to-end on the real Azure Queue transport:
/// a command raises an event, the framework publish span fires from the outbox
/// publish executor, the deferred-invocation span (raised when the
/// InvokeHandler executor drains the staged entry) hangs off the publish
/// span — same trace id, parent-child by span id. Re-proving this against
/// real Azure Queue Storage guards the captured-traceparent propagation
/// across the actual queue hop (CLAUDE.md keeps one end-to-end span-stitch
/// assertion in Azure.Tests to prove the transport preserves trace headers).
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class EventHandlerSpanStitchAcrossOutboxHopTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task DeferredInvocationSpan_ShouldNestUnderPublishSpan_AcrossAzureQueueHop()
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

        await fixture.Sender.Send(new AzureNotifyCustomerCommand(customerId, "welcome"));

        var handler = fixture.Cluster.GrainFactory.GetGrain<IAzureEmailHandlerProbe>(customerId);
        await EventHandlerWaiters.WaitForHandledAsync(handler);

        // Give the InvokeHandlerExecutor a moment to close the handle span —
        // the probe's count increments inside Handle, but ActivityStopped only
        // fires after the executor's using-scope unwinds.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Activity publishSpan;
        Activity invocationSpan;
        lock (stopped)
        {
            publishSpan = stopped.First(a =>
                a.OperationName == "edict.event.publish AzureCustomerNotifiedEvent");
            invocationSpan = stopped.First(a =>
                a.OperationName == "edict.event.handle AzureCustomerNotifiedEvent"
                && a.ParentSpanId == publishSpan.SpanId);
        }

        Assert.Equal(publishSpan.TraceId, invocationSpan.TraceId);
    }
}
