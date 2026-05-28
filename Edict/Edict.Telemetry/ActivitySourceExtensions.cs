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

    public static Activity? StartEdictEventPublish(
        this ActivitySource source,
        string eventTypeName,
        ActivityContext parentContext)
        => source.StartActivity(
            $"{SemanticConventions.Events.Spans.Publish} {eventTypeName}",
            ActivityKind.Producer,
            parentContext);

    public static Activity? StartEdictEventHandle(
        this ActivitySource source,
        string eventTypeName,
        ActivityContext parentContext)
        => source.StartActivity(
            $"{SemanticConventions.Events.Spans.Handle} {eventTypeName}",
            ActivityKind.Consumer,
            parentContext);

    public static Activity? StartEdictEventDeduplicated(
        this ActivitySource source,
        string eventTypeName,
        ActivityContext parentContext)
        => source.StartActivity(
            $"{SemanticConventions.Events.Spans.Deduplicated} {eventTypeName}",
            ActivityKind.Consumer,
            parentContext);

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
