using Edict.Contracts.Configuration;

namespace Edict.Core.Outbox;

/// <summary>
/// Exponential backoff as a pure function of <c>AttemptCount</c> (ADR 0019).
/// No schedule is persisted beyond the per-entry <c>AttemptCount</c>/
/// <c>NextAttemptUtc</c>; the same one lazy Reminder gates retries on the
/// timestamp, so there is no second scheduling primitive. The delay is clamped
/// to the configured ceiling so a multi-hour outage cannot push the next
/// attempt arbitrarily far out, then spread by a <b>deterministic</b> per-entry
/// jitter (a stable hash of <c>EntryId</c>, never a clock or RNG) so a fleet of
/// entries that fail together do not stampede the same retry instant while the
/// function stays pure and reproducible.
/// </summary>
public static class OutboxBackoff
{
    /// <summary>
    /// The earliest UTC instant the entry may be retried. <paramref name="attemptCount"/>
    /// is the post-increment count of the attempt that just failed; <c>&lt;= 0</c>
    /// means "no prior failure" and yields <paramref name="now"/> (drain immediately).
    /// <paramref name="entryId"/> seeds the deterministic jitter so the same
    /// entry is reproducible while sibling entries spread apart.
    /// </summary>
    public static DateTimeOffset NextAttemptUtc(
        int attemptCount, DateTimeOffset now, Guid entryId, EdictOptions options)
    {
        if (attemptCount <= 0)
        {
            return now;
        }

        var exponent = attemptCount - 1;
        // Cap the exponent so the shift cannot overflow; anything past the
        // ceiling clamps to OutboxMaxDelay regardless.
        var factor = exponent >= 30 ? double.PositiveInfinity : 1L << exponent;
        var delaySeconds = options.OutboxBaseDelay.TotalSeconds * factor;

        var delay = delaySeconds >= options.OutboxMaxDelay.TotalSeconds
            ? options.OutboxMaxDelay
            : TimeSpan.FromSeconds(delaySeconds);

        var jittered = ApplyJitter(delay, entryId, options.OutboxJitterFraction, options.OutboxMaxDelay);
        return now + jittered;
    }

    /// <summary>
    /// Spreads <paramref name="delay"/> by a deterministic ±<paramref name="fraction"/>
    /// offset derived from <paramref name="entryId"/>. Same entry → same offset
    /// (reproducible); different entries → different offsets (no stampede).
    /// Clamped to <c>[0, maxDelay]</c> so jitter cannot breach the ceiling.
    /// </summary>
    static TimeSpan ApplyJitter(TimeSpan delay, Guid entryId, double fraction, TimeSpan maxDelay)
    {
        var clampedFraction = Math.Clamp(fraction, 0d, 1d);
        if (clampedFraction == 0d)
        {
            return delay;
        }

        // Stable [0,1) unit from a 64-bit FNV-1a hash over all 16 EntryId
        // bytes (mixing every byte so sibling Guids spread, not just the low
        // word), mapped to a signed multiplier in [-1, +1]. No RNG, no clock.
        var unit = Fnv1a64(entryId) / (double)ulong.MaxValue;
        var signed = (unit * 2d) - 1d;

        var jittered = delay.TotalSeconds * (1d + (clampedFraction * signed));
        var bounded = Math.Clamp(jittered, 0d, maxDelay.TotalSeconds);
        return TimeSpan.FromSeconds(bounded);
    }

    static ulong Fnv1a64(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);

        ulong hash = 14695981039346656037; // FNV offset basis
        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= 1099511628211; // FNV prime
        }

        return hash;
    }
}
