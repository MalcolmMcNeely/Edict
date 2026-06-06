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

    public static Activity? StartEdictEventPublish(
        this ActivitySource source,
        string eventTypeName,
        ActivityContext parentContext)
        => source.StartActivity(
            $"{SemanticConventions.Events.Spans.Publish} {eventTypeName}",
            ActivityKind.Producer,
            parentContext);

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

    public static Activity? StartEdictTableUpsert(
        this ActivitySource source,
        string tableName,
        ActivityContext parentContext)
        => source.StartActivity(
            $"{SemanticConventions.Tables.Spans.Upsert} {tableName}",
            ActivityKind.Client,
            parentContext);
}
