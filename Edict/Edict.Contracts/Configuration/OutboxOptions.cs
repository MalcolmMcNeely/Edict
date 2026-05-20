namespace Edict.Contracts.Configuration;

/// <summary>
/// Consumer-tunable Outbox/dead-letter policy (ADR 0018 / 0022). The framework
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
    /// Attempts before a permanently failing entry is promoted to a dead-letter
    /// publish (ADR 0022). The failing attempt that reaches this count removes
    /// the entry from the Outbox and appends a new
    /// <c>EdictDeadLetterRaised</c> publish entry at the FIFO tail.
    /// </summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>
    /// Fraction of the computed delay used as a deterministic ±spread per entry
    /// so a fleet failing together does not stampede the same retry instant.
    /// <c>0</c> disables jitter. Clamped to <c>[0, 1]</c>.
    /// </summary>
    public double JitterFraction { get; set; } = 0.2;
}
