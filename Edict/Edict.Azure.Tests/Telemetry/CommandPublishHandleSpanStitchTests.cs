using System.Diagnostics;

using Edict.Telemetry;
using Edict.Tests.Conformance.EventHandler;

using Orleans;

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

        await fixture.Sender.Send(new NotifyCustomerCommand(customerId, "welcome"));

        var handler = fixture.Cluster.GrainFactory.GetGrain<IEmailHandlerProbe>(customerId);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline && await handler.GetHandledCountAsync() == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        // The probe's count increments inside Handle, but ActivityStopped
        // only fires after the executor's using-scope unwinds.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Activity commandSpan;
        Activity publishSpan;
        Activity handleSpan;
        lock (stopped)
        {
            commandSpan = stopped.First(a =>
                a.OperationName == $"{SemanticConventions.Commands.Spans.Command} NotifyCustomerCommand"
                && customerId.Equals(a.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)));
            publishSpan = stopped.First(a =>
                a.OperationName == $"{SemanticConventions.Events.Spans.Publish} CustomerNotifiedEvent"
                && a.ParentSpanId == commandSpan.SpanId);
            handleSpan = stopped.First(a =>
                a.OperationName == $"{SemanticConventions.Events.Spans.Handle} CustomerNotifiedEvent"
                && a.ParentSpanId == publishSpan.SpanId);
        }

        Assert.Equal(commandSpan.TraceId, publishSpan.TraceId);
        Assert.Equal(commandSpan.TraceId, handleSpan.TraceId);
    }
}

