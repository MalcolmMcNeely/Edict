using System.Diagnostics;

using Edict.Telemetry;

using Xunit;

namespace Edict.Core.Tests.Schedules;

// A schedule fire is its own grain turn, decoupled in time from the command that
// armed it, so edict.schedule.fire is a new trace root that links back to the
// arming command rather than nesting under it. The arm-context the link rides on
// is captured at Schedule() and persisted on the durable ScheduleEntry, so the
// link survives a deactivation between arming and firing.
[Collection(ScheduleLifecycleCollection.Name)]
public sealed class ScheduleFireSpanTests
{
    static readonly TimeSpan Cadence = TimeSpan.FromHours(1);

    readonly ScheduleLifecycleClusterFixture _fixture;

    public ScheduleFireSpanTests(ScheduleLifecycleClusterFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Fire_ShouldBeNewRootLinkingToTheArmingCommand()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { lock (stopped) { stopped.Add(activity); } },
        };
        ActivitySource.AddActivityListener(listener);

        var probeId = Guid.NewGuid();
        var probe = _fixture.GrainFactory.GetGrain<IScheduleProbe>(probeId.ToString("N"));
        await probe.StartAsync(Cadence, ScheduleProbeBehavior.ContinueNoEvent);

        _fixture.AdvanceClock(Cadence);
        await probe.FireDueSchedulesAsync();

        Activity armSpan;
        Activity fireSpan;
        lock (stopped)
        {
            // The arming command carries this probe's key; sibling schedule tests share
            // the cluster and their leftover schedules fire on the same advanced clock,
            // so the fire is scoped by the link back to this arm rather than by name.
            armSpan = stopped.Single(a =>
                a.OperationName == $"{SemanticConventions.Commands.Spans.Handle} ScheduleProbeStartCommand"
                && probeId.ToString("N").Equals(a.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)));
            fireSpan = stopped.Single(a =>
                a.OperationName == $"{SemanticConventions.Schedules.Spans.Fire} ScheduleTickMessage"
                && a.Links.Any(link => link.Context.SpanId == armSpan.SpanId));
        }

        // The fire is its own trace — it does not share the arming command's.
        Assert.Null(fireSpan.Parent);
        Assert.NotEqual(armSpan.TraceId, fireSpan.TraceId);

        // ...but it links back to the command that armed the schedule.
        var onlyLink = Assert.Single(fireSpan.Links);
        Assert.Equal(armSpan.TraceId, onlyLink.Context.TraceId);
        Assert.Equal(armSpan.SpanId, onlyLink.Context.SpanId);
    }

    [Fact]
    public async Task Fire_ShouldCarryAFreshConversationId_DistinctFromTheArmingCommand()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EdictDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => { lock (stopped) { stopped.Add(activity); } },
        };
        ActivitySource.AddActivityListener(listener);

        var probeId = Guid.NewGuid();
        var probe = _fixture.GrainFactory.GetGrain<IScheduleProbe>(probeId.ToString("N"));
        await probe.StartAsync(Cadence, ScheduleProbeBehavior.ContinueNoEvent);

        _fixture.AdvanceClock(Cadence);
        await probe.FireDueSchedulesAsync();

        Activity armSpan;
        Activity fireSpan;
        lock (stopped)
        {
            armSpan = stopped.Single(a =>
                a.OperationName == $"{SemanticConventions.Commands.Spans.Handle} ScheduleProbeStartCommand"
                && probeId.ToString("N").Equals(a.GetTagItem(SemanticConventions.Commands.Tags.RouteKey)));
            fireSpan = stopped.Single(a =>
                a.OperationName == $"{SemanticConventions.Schedules.Spans.Fire} ScheduleTickMessage"
                && a.Links.Any(link => link.Context.SpanId == armSpan.SpanId));
        }

        // A timer fire is a deliberate fresh causal root, so it mints its own
        // conversation rather than inheriting the arming command's — the link, not the
        // conversation id, is what ties the recurring job back to who armed it.
        var fireConversationId = Assert.IsType<string>(fireSpan.GetTagItem(SemanticConventions.Messaging.Tags.ConversationId));
        Assert.NotEqual(Guid.Empty, Guid.Parse(fireConversationId));
        Assert.NotEqual(armSpan.GetTagItem(SemanticConventions.Messaging.Tags.ConversationId), fireConversationId);
    }
}
