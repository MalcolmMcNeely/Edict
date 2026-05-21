using System.Diagnostics;

using Edict.Azure.Tests.EventHandler;
using Edict.Telemetry;

namespace Edict.Azure.Tests.Telemetry;

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

        // The probe's count increments inside Handle, but ActivityStopped
        // only fires after the executor's using-scope unwinds.
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
