using System.Diagnostics;

using Edict.Telemetry;
using Edict.Tests.Conformance.EventHandler;

using Orleans;

namespace Edict.Azure.Streaming.Tests.Telemetry;

[Collection(AqsStreamingCollection.Name)]
public sealed class CommandPublishHandleSpanStitchTests(AqsStreamingFixture fixture)
{
    [Fact]
    public async Task CommandPublishParentChild_AndHandleLinksToPublish_AcrossAzureQueueHop()
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

        await fixture.Sender.SendAsync(new NotifyCustomerCommand(customerId, "welcome"));

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
            // Scope to the handle span that links to this publish — the link both
            // identifies it across the hop and is the property under test.
            handleSpan = stopped.First(a =>
                a.OperationName == $"{SemanticConventions.Events.Spans.Handle} CustomerNotifiedEvent"
                && a.Links.Any(link => link.Context.SpanId == publishSpan.SpanId));
        }

        // Command -> publish stays one parent-child trace (a synchronous turn).
        Assert.Equal(commandSpan.TraceId, publishSpan.TraceId);

        // The consumer turn is its own trace, linked back to publish across the hop.
        Assert.Equal(default, handleSpan.ParentSpanId);
        Assert.NotEqual(commandSpan.TraceId, handleSpan.TraceId);
        Assert.Equal(publishSpan.TraceId, handleSpan.Links.Single().Context.TraceId);
    }
}

