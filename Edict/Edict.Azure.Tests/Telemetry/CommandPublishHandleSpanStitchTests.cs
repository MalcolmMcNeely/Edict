using System.Diagnostics;

using Edict.Azure.Tests.EventHandler;
using Edict.Telemetry;

namespace Edict.Azure.Tests.Telemetry;

/// <summary>
/// ADR 0003 end-to-end span-stitch proof on the real Azure Queue Storage
/// transport: dispatching a command through <see cref="Sender"/> opens a
/// Command span, the framework publish path opens a Publish span under it,
/// and the deferred-invocation Handle span (raised after the dispatch
/// drains the staged InvokeHandler entry across the Azurite queue hop)
/// hangs off the Publish span — same <c>TraceId</c>, parent-child by
/// <c>SpanId</c>. CLAUDE.md keeps exactly one of these against the real
/// transport (the in-memory equivalent lives in
/// <c>Edict.Telemetry.Tests/CommandSpanTests</c>); this guards against the
/// transport silently dropping trace headers across <c>RequestContext</c>
/// serialization.
/// </summary>
[Collection(AzureClusterCollection.Name)]
public sealed class CommandPublishHandleSpanStitchTests(AzureClusterFixture fixture)
{
    [Fact]
    public async Task CommandPublishHandleSpans_ShouldFormParentChildTree_AcrossAzureQueueHop()
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

        Activity commandSpan;
        Activity publishSpan;
        Activity handleSpan;
        lock (stopped)
        {
            commandSpan = stopped.First(a =>
                a.OperationName == "edict.command AzureNotifyCustomerCommand"
                && customerId.Equals(a.GetTagItem("edict.command.route_key")));
            publishSpan = stopped.First(a =>
                a.OperationName == "edict.event.publish AzureCustomerNotifiedEvent"
                && a.ParentSpanId == commandSpan.SpanId);
            handleSpan = stopped.First(a =>
                a.OperationName == "edict.event.handle AzureCustomerNotifiedEvent"
                && a.ParentSpanId == publishSpan.SpanId);
        }

        Assert.Equal(commandSpan.TraceId, publishSpan.TraceId);
        Assert.Equal(commandSpan.TraceId, handleSpan.TraceId);
    }
}
