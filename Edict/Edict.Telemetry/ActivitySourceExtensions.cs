using System.Diagnostics;

namespace Edict.Telemetry;

/// <summary>
/// Extension methods on <see cref="ActivitySource"/> for starting the named Edict
/// spans: command dispatch, event publish, and dedup.
/// </summary>
public static class ActivitySourceExtensions
{
    public static Activity? StartEdictCommand(this ActivitySource source, string operationName)
        => source.StartActivity(operationName);

    public static Activity? StartEdictCommandHandle(
        this ActivitySource source,
        string commandTypeName,
        ActivityContext parentContext)
        => source.StartActivity(
            $"{SemanticConventions.Commands.Spans.Handle} {commandTypeName}",
            ActivityKind.Server,
            parentContext);

    /// <summary>
    /// Starts <c>edict.command.handle</c> as a new trace root carrying one
    /// <see cref="ActivityLink"/> back to the <c>edict.command.send</c> context — the
    /// per-turn form for a saga's fire-and-forget dispatch, where the handled command
    /// is its own grain turn rather than a child of the producing turn. A
    /// <see langword="default"/> <paramref name="linkContext"/> starts a bare root.
    /// </summary>
    public static Activity? StartEdictCommandHandleLinked(
        this ActivitySource source,
        string commandTypeName,
        ActivityContext linkContext)
        => StartNewRoot(
            source,
            $"{SemanticConventions.Commands.Spans.Handle} {commandTypeName}",
            ActivityKind.Server,
            linkContext.TraceId == default ? null : new ActivityLink(linkContext));

    public static Activity? StartEdictEventPublish(
        this ActivitySource source,
        string eventTypeName,
        ActivityContext parentContext)
        => source.StartActivity(
            $"{SemanticConventions.Events.Spans.Publish} {eventTypeName}",
            ActivityKind.Producer,
            parentContext);

    /// <summary>
    /// Starts <c>edict.event.publish</c> as a new trace root carrying one
    /// <see cref="ActivityLink"/> back to the staging command's context — the
    /// per-turn form for a recovery drain (reminder / activation), where the entry
    /// was rehydrated from durable state long after the staging turn closed and so
    /// publishing it is its own grain turn rather than a child of the producer. A
    /// <see langword="null"/> <paramref name="link"/> (the entry carried no trace
    /// context) starts a bare root. The inline drain that still holds the live event
    /// reference uses <see cref="StartEdictEventPublish"/> and stays a child.
    /// </summary>
    public static Activity? StartEdictEventPublishLinked(
        this ActivitySource source,
        string eventTypeName,
        ActivityLink? link)
        => StartNewRoot(
            source,
            $"{SemanticConventions.Events.Spans.Publish} {eventTypeName}",
            ActivityKind.Producer,
            link);

    /// <summary>
    /// Starts the consumer-side <c>edict.event.handle</c> span as a new trace root
    /// carrying one <see cref="ActivityLink"/> back to the publish span — the
    /// per-turn invariant bounds each consumer turn to its own trace while keeping
    /// the producer pivotable through the link. A <see langword="null"/>
    /// <paramref name="link"/> (event carried no trace context) starts a bare root.
    /// </summary>
    public static Activity? StartEdictEventHandle(
        this ActivitySource source,
        string eventTypeName,
        ActivityLink? link)
        => StartNewRoot(
            source,
            $"{SemanticConventions.Events.Spans.Handle} {eventTypeName}",
            ActivityKind.Consumer,
            link);

    // Starts a root span even when an ambient activity is in scope. Passing
    // parentContext: default is not enough — StartActivity falls back to
    // Activity.Current when the supplied context is empty, which would nest the
    // consumer turn under whatever happened to flow in. Clearing Current for the
    // duration of the start forces a fresh trace; the new span becomes Current
    // and owns the turn, so the only time the ambient is restored is when no
    // listener created a span.
    static Activity? StartNewRoot(ActivitySource source, string name, ActivityKind kind, ActivityLink? link)
    {
        var ambient = Activity.Current;
        Activity.Current = null;
        try
        {
            return source.StartActivity(
                name,
                kind,
                parentContext: default,
                tags: null,
                links: link is { } value ? [value] : null);
        }
        finally
        {
            if (Activity.Current is null)
            {
                Activity.Current = ambient;
            }
        }
    }

    /// <summary>
    /// Starts the <c>edict.event.deduplicated</c> span as a new trace root with one
    /// <see cref="ActivityLink"/> back to the publish span. A suppressed redelivery
    /// is its own consumer turn under the per-turn invariant, so it gets its own
    /// bounded trace that links to the publish it duplicates.
    /// </summary>
    public static Activity? StartEdictEventDeduplicated(
        this ActivitySource source,
        string eventTypeName,
        ActivityLink? link)
        => StartNewRoot(
            source,
            $"{SemanticConventions.Events.Spans.Deduplicated} {eventTypeName}",
            ActivityKind.Consumer,
            link);

    public static Activity? StartEdictCommandSend(
        this ActivitySource source,
        string commandTypeName,
        ActivityContext parentContext)
        => source.StartActivity(
            $"{SemanticConventions.Commands.Spans.Send} {commandTypeName}",
            ActivityKind.Producer,
            parentContext);

    /// <summary>
    /// Starts <c>edict.schedule.fire</c> as a new trace root carrying one
    /// <see cref="ActivityLink"/> back to the command that armed the schedule. A fire
    /// is its own grain turn, decoupled in time from the arming command, so the
    /// per-turn invariant bounds it to its own trace while the arm-context keeps the
    /// originator navigable. A <see langword="null"/> <paramref name="link"/> (the
    /// schedule was armed with no trace context) starts a bare root.
    /// </summary>
    public static Activity? StartEdictScheduleFire(
        this ActivitySource source,
        string scheduleMessageTypeName,
        ActivityLink? link)
        => StartNewRoot(
            source,
            $"{SemanticConventions.Schedules.Spans.Fire} {scheduleMessageTypeName}",
            ActivityKind.Internal,
            link);

    /// <summary>
    /// Starts <c>edict.saga.timeout</c> as a new trace root carrying one
    /// <see cref="ActivityLink"/> back to the context that armed the saga on its
    /// first handled event. A fired cap is its own grain turn, far removed in time
    /// from the arming event, so it gets its own bounded trace that links to the
    /// arm-context. A <see langword="null"/> <paramref name="link"/> starts a bare root.
    /// </summary>
    public static Activity? StartEdictSagaTimeout(
        this ActivitySource source,
        string sagaTypeName,
        ActivityLink? link)
        => StartNewRoot(
            source,
            $"{SemanticConventions.Sagas.Spans.Timeout} {sagaTypeName}",
            ActivityKind.Internal,
            link);

    /// <summary>
    /// Starts <c>edict.dead_letter.promote</c> as a new trace root carrying one
    /// <see cref="ActivityLink"/> back to the failing entry's originating context.
    /// Promotion runs in a decoupled drain turn, not the command turn that produced
    /// the failure, so a link — not a parent — is the correct relationship, and an
    /// operator can pivot from a promoted dead-letter to the originating command
    /// trace. A <see langword="null"/> <paramref name="link"/> (the entry carried no
    /// trace context) starts a bare root. This sits on the safety-net path: the start,
    /// any <c>SetTag</c>, and <c>Dispose</c> must not throw.
    /// </summary>
    public static Activity? StartEdictDeadLetterPromote(
        this ActivitySource source,
        string sourceGrainType,
        ActivityLink? link)
        => StartNewRoot(
            source,
            $"{SemanticConventions.DeadLetter.Spans.Promote} {sourceGrainType}",
            ActivityKind.Internal,
            link);

    public static Activity? StartEdictTableUpsert(
        this ActivitySource source,
        string tableName,
        ActivityContext parentContext)
        => source.StartActivity(
            $"{SemanticConventions.Tables.Spans.Upsert} {tableName}",
            ActivityKind.Client,
            parentContext);
}
