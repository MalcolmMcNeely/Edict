namespace Edict.Contracts.Configuration;

/// <summary>
/// Silo-wide Command Handler schedule knobs, sibling to <see cref="EdictSagaOptions"/>
/// and tuned the same way: a property with a default, a <c>ValidateOnStart</c>
/// rule, and a line in <c>Sample.Azure.Silo/Program.cs</c>. The class name carries
/// the scope: this default applies to schedules a Command Handler arms, not to a
/// Saga's schedules (those inherit the saga's own cap). Brand-prefixed because the
/// consumer types it. Configured under the existing <c>AddEdict()</c>.
/// </summary>
public sealed class EdictCommandHandlerScheduleOptions
{
    /// <summary>
    /// The timeout cap applied to any Command Handler schedule started without an
    /// explicit <c>timeout:</c>. Ships finite at 7 days (matching
    /// <see cref="EdictSagaOptions.DefaultTimeout"/>) so no schedule can tick
    /// forever by accident. Set to <c>null</c> to return the silo to uncapped
    /// command-handler schedules (a schedule then runs until it <c>Complete()</c>s
    /// or carries its own <c>timeout:</c>). A per-schedule
    /// <c>timeout: EdictSchedule.Unbounded</c> always beats this default.
    /// </summary>
    public TimeSpan? DefaultTimeout { get; set; } = TimeSpan.FromDays(7);
}
