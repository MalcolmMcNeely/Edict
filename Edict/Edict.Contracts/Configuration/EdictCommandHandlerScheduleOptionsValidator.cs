using Edict.Contracts.Schedules;

namespace Edict.Contracts.Configuration;

// Pure validation surface over EdictCommandHandlerScheduleOptions, mirroring
// EdictSagaOptionsValidator so EdictWiringValidator aggregates a schedule-options
// failure into the same startup EdictWiringException. A null DefaultTimeout is
// valid (uncapped); the Unbounded sentinel is valid too (an explicit silo-wide
// opt-out); any other non-null value must be a positive duration — a zero or
// negative cap is a typo, never a silent clamp.
internal static class EdictCommandHandlerScheduleOptionsValidator
{
    public static IReadOnlyList<string> Validate(EdictCommandHandlerScheduleOptions options)
    {
        var failures = new List<string>();

        if (options.DefaultTimeout is { } timeout && timeout != EdictSchedule.Unbounded && timeout <= TimeSpan.Zero)
        {
            failures.Add(
                $"{nameof(EdictCommandHandlerScheduleOptions.DefaultTimeout)} must be greater than zero "
                + $"(or null to opt out) but was {timeout}.");
        }

        return failures;
    }
}
