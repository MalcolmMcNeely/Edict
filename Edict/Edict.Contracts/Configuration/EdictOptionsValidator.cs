namespace Edict.Contracts.Configuration;

/// <summary>
/// Pure validation surface over the framework's options classes (ADR 0028).
/// Returns the full list of failure messages per invocation — a host with two
/// problems sees two problems — so the <see cref="EdictWiringValidator"/>
/// hosted service can aggregate every option-bag's failures into one
/// <see cref="InvalidOperationException"/> at <c>StartAsync</c>. No DI access;
/// runs identically under the in-memory Test Framework and a production silo.
/// </summary>
public static class EdictOptionsValidator
{
    public static IReadOnlyList<string> Validate(EdictOptions options)
    {
        var failures = new List<string>();

        if (options.OutboxJitterFraction < 0 || options.OutboxJitterFraction > 1)
        {
            failures.Add(
                $"{nameof(EdictOptions.OutboxJitterFraction)} must be in the range [0, 1] but was {options.OutboxJitterFraction}.");
        }

        if (options.OutboxMaxAttempts < 1)
        {
            failures.Add(
                $"{nameof(EdictOptions.OutboxMaxAttempts)} must be at least 1 but was {options.OutboxMaxAttempts}.");
        }

        if (options.OutboxBaseDelay <= TimeSpan.Zero)
        {
            failures.Add(
                $"{nameof(EdictOptions.OutboxBaseDelay)} must be greater than zero but was {options.OutboxBaseDelay}.");
        }
        else if (options.OutboxBaseDelay > options.OutboxMaxDelay)
        {
            failures.Add(
                $"{nameof(EdictOptions.OutboxBaseDelay)} ({options.OutboxBaseDelay}) must not exceed " +
                $"{nameof(EdictOptions.OutboxMaxDelay)} ({options.OutboxMaxDelay}).");
        }

        if (options.OutboxDrainReminderPeriod < TimeSpan.FromMinutes(1))
        {
            failures.Add(
                $"{nameof(EdictOptions.OutboxDrainReminderPeriod)} must be at least one minute " +
                $"(Orleans' reminder floor) but was {options.OutboxDrainReminderPeriod}.");
        }

        if (options.IdempotencyWindowSize < 1)
        {
            failures.Add(
                $"{nameof(EdictOptions.IdempotencyWindowSize)} must be at least 1 but was {options.IdempotencyWindowSize}.");
        }

        return failures;
    }
}
