namespace Edict.Core.Outbox;

/// <summary>
/// Exponential backoff as a pure function of <c>AttemptCount</c> (ADR 0019).
/// No schedule is persisted beyond the per-entry <c>AttemptCount</c>/
/// <c>NextAttemptUtc</c>; the same one lazy Reminder gates retries on the
/// timestamp, so there is no second scheduling primitive. The delay is clamped
/// to a ceiling so a multi-hour outage cannot push the next attempt arbitrarily
/// far out.
/// </summary>
public static class OutboxBackoff
{
    static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The earliest UTC instant the entry may be retried. <paramref name="attemptCount"/>
    /// is the post-increment count of the attempt that just failed; <c>&lt;= 0</c>
    /// means "no prior failure" and yields <paramref name="now"/> (drain immediately).
    /// </summary>
    public static DateTimeOffset NextAttemptUtc(int attemptCount, DateTimeOffset now, TimeSpan baseDelay)
    {
        if (attemptCount <= 0)
        {
            return now;
        }

        var exponent = attemptCount - 1;
        // Cap the exponent so the shift cannot overflow; anything past the
        // ceiling clamps to MaxDelay regardless.
        var factor = exponent >= 30 ? double.PositiveInfinity : 1L << exponent;
        var delaySeconds = baseDelay.TotalSeconds * factor;

        var delay = delaySeconds >= MaxDelay.TotalSeconds
            ? MaxDelay
            : TimeSpan.FromSeconds(delaySeconds);

        return now + delay;
    }
}
