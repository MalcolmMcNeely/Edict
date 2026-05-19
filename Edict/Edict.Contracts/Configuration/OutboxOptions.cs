namespace Edict.Contracts.Configuration;

/// <summary>
/// Consumer-tunable Outbox/dead-letter policy (ADR 0018 / 0019). The framework
/// ships sensible defaults; a consumer overrides only what it needs via
/// <c>AddEdictOutbox(configure)</c>. Plain options POCO — no Orleans runtime
/// dependency, so it lives in the shared kernel. Brand-prefixed because the
/// consumer types it.
/// </summary>
public sealed class EdictOutboxOptions
{
    /// <summary>The first retry delay; doubles each attempt up to <see cref="MaxDelay"/>.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>The backoff ceiling — a long outage cannot push the next attempt past this.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Attempts before a permanently failing entry is dead-lettered. The
    /// failing attempt that reaches this count moves the entry Outbox→DeadLetter.
    /// </summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>
    /// Maximum dead-lettered entries retained in the grain document before the
    /// grain blocks intake (commands fault, redelivered events are not acked).
    /// </summary>
    public int DeadLetterCap { get; set; } = 100;

    /// <summary>
    /// Fraction of the computed delay used as a deterministic ±spread per entry
    /// so a fleet failing together does not stampede the same retry instant.
    /// <c>0</c> disables jitter. Clamped to <c>[0, 1]</c>.
    /// </summary>
    public double JitterFraction { get; set; } = 0.2;
}
