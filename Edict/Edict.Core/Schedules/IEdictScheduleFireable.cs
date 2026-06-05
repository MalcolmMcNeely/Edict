using Orleans;

namespace Edict.Core.Schedules;

/// <summary>
/// The framework-internal fire surface every Command Handler exposes for the
/// Test Framework's deterministic <c>FireDueSchedulesAsync</c> seam. Kept off
/// <c>IEdictCommandHandler</c> so the command invokable is undisturbed. No human
/// authors or reads this; production firing flows through the grain timer and
/// Reminder, not this interface.
/// </summary>
public interface IEdictScheduleFireable : IGrainWithGuidKey
{
    /// <summary>Fires every schedule due at the current framework clock.</summary>
    Task FireDueSchedulesAsync();

    /// <summary>The soonest due instant across this grain's active schedules, or null when none.</summary>
    Task<DateTimeOffset?> PeekSoonestScheduleDueAsync();

    /// <summary>Fires every schedule whose timeout cap is now-or-past at the current framework clock.</summary>
    Task FireDueScheduleTimeoutsAsync();

    /// <summary>The soonest timeout-cap instant across this grain's capped schedules, or null when none are capped.</summary>
    Task<DateTimeOffset?> PeekSoonestScheduleTimeoutAsync();
}
