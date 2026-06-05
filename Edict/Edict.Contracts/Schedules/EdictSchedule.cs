namespace Edict.Contracts.Schedules;

/// <summary>
/// Concept holder for the EdictSchedule timeout vocabulary. Holds the
/// <see cref="Unbounded"/> opt-out a consumer passes to
/// <c>Schedule(message, every, timeout: EdictSchedule.Unbounded)</c>. Named so
/// that "forever" is always a deliberate, visible choice rather than something a
/// consumer falls into by forgetting an argument.
/// </summary>
public static class EdictSchedule
{
    /// <summary>
    /// Opts a schedule out of any timeout cap. A branded sentinel value, distinct
    /// from <c>null</c>: passing <c>null</c> (or omitting <c>timeout:</c>) inherits
    /// the silo default, whereas this explicitly disables the cap so a legitimately
    /// perpetual schedule (a subscription renewal, a lease) runs until it
    /// <c>Complete()</c>s. Exempt from the positive-duration validation rule.
    /// </summary>
    public static readonly TimeSpan Unbounded = TimeSpan.MinValue;
}
