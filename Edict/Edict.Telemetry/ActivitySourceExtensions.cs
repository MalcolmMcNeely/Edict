using System.Diagnostics;

namespace Edict.Telemetry;

/// <summary>
/// Extension methods on <see cref="ActivitySource"/> for starting the named Edict
/// spans: command dispatch, event publish, and dedup (ADR 0003).
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
            $"edict.event.publish {eventTypeName}",
            ActivityKind.Producer,
            parentContext);

    public static Activity? StartEdictEventHandle(
        this ActivitySource source,
        string eventTypeName,
        ActivityContext parentContext)
        => source.StartActivity(
            $"edict.event.handle {eventTypeName}",
            ActivityKind.Consumer,
            parentContext);

    public static Activity? StartEdictEventDeduplicated(
        this ActivitySource source,
        string eventTypeName,
        ActivityContext parentContext)
        => source.StartActivity(
            $"edict.event.deduplicated {eventTypeName}",
            ActivityKind.Consumer,
            parentContext);

    public static Activity? StartEdictCommandSend(
        this ActivitySource source,
        string commandTypeName,
        ActivityContext parentContext)
        => source.StartActivity(
            $"edict.command.send {commandTypeName}",
            ActivityKind.Producer,
            parentContext);

    public static Activity? StartEdictTableUpsert(
        this ActivitySource source,
        string tableName,
        ActivityContext parentContext)
        => source.StartActivity(
            $"edict.table.upsert {tableName}",
            ActivityKind.Client,
            parentContext);
}
