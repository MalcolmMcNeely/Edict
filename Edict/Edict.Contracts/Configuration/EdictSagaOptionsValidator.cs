namespace Edict.Contracts.Configuration;

// Pure validation surface over EdictSagaOptions, mirroring EdictOptionsValidator
// so EdictWiringValidator aggregates a saga-options failure into the same
// startup InvalidOperationException. A null DefaultTimeout is valid (fully
// opt-in); a non-null value must be a positive duration — a zero or negative
// cap is a typo, never a silent clamp.
internal static class EdictSagaOptionsValidator
{
    public static IReadOnlyList<string> Validate(EdictSagaOptions options)
    {
        var failures = new List<string>();

        if (options.DefaultTimeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            failures.Add(
                $"{nameof(EdictSagaOptions.DefaultTimeout)} must be greater than zero (or null to opt out) but was {timeout}.");
        }

        return failures;
    }
}
