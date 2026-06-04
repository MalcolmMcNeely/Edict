using MessagePack;

namespace Edict.Contracts.Schedules;

/// <summary>
/// The outcome a schedule fire handler returns: a closed hierarchy of exactly
/// <see cref="Continue"/> (fire again on the declared cadence) and
/// <see cref="Complete"/> (stop the schedule). The private constructor closes
/// the hierarchy so only the nested variants can derive from it. The handler
/// answers only "again or done"; the cadence lives at the <c>Schedule(...)</c>
/// call site, never here.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public abstract record EdictScheduleResult
{
    EdictScheduleResult()
    {
    }

    /// <summary>Fire again on the declared cadence.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed record Continue : EdictScheduleResult;

    /// <summary>Stop the schedule; no further fires.</summary>
    [MessagePackObject(keyAsPropertyName: true)]
    public sealed record Complete : EdictScheduleResult;
}
