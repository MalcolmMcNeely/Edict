using System.Diagnostics;

using Edict.Telemetry;
using Edict.Tests.Conformance.EventHandler;

using Xunit;

namespace Edict.Tests.Conformance.Streaming;

/// <summary>
/// Streaming-axis conformance that the deferred <c>edict.event.handle</c> span is
/// a new trace root carrying one <see cref="ActivityLink"/> back to the
/// originating <c>edict.event.publish</c> span across the stream hop. Under the
/// per-turn model the consumer turn is its own bounded trace, so the link — not a
/// shared trace — is what survives the real stream hop, a substrate-dependent
/// property since the publish span's identity must ride the event across it.
/// </summary>
public abstract class EventHandlerSpanStitchAcrossOutboxHopScenarios<TFixture>
    where TFixture : StreamingConformanceFixture
{
    readonly TFixture _fixture;

    protected EventHandlerSpanStitchAcrossOutboxHopScenarios(TFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DeferredInvocationSpan_ShouldBeNewRootLinkingToPublishSpan_AcrossStreamHop()
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

        await _fixture.Sender.SendAsync(new NotifyCustomerCommand(customerId, "welcome"));

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
                a.OperationName == $"{SemanticConventions.Events.Spans.Publish} CustomerNotifiedEvent");
            // Scope to the handle span that links to this publish — the link both
            // identifies it across the hop and is the property under test.
            invocationSpan = stopped.First(a =>
                a.OperationName == $"{SemanticConventions.Events.Spans.Handle} CustomerNotifiedEvent"
                && a.Links.Any(link => link.Context.SpanId == publishSpan.SpanId));
        }

        // A new trace root, not a child of publish, but linked back to it.
        Assert.Equal(default, invocationSpan.ParentSpanId);
        Assert.NotEqual(publishSpan.TraceId, invocationSpan.TraceId);
        Assert.Equal(publishSpan.TraceId, invocationSpan.Links.Single().Context.TraceId);
    }
}
