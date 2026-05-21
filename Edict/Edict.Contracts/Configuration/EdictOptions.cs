namespace Edict.Contracts.Configuration;

/// <summary>
/// Flat, consumer-tunable knobs over the framework's core mechanisms.
/// Every constructor default is the literal previously embedded in mechanism
/// code — moving those literals here is the principle the ADR pins down:
/// every framework knob is a property on an options class with a default, a
/// <c>ValidateOnStart</c> rule, and a line in <c>Sample.Silo/Program.cs</c>,
/// never a literal in mechanism code. Brand-prefixed because the consumer
/// types it.
/// </summary>
public sealed class EdictOptions
{
    /// <summary>
    /// Maximum number of distinct <c>EdictEvent.EventId</c> values remembered
    /// per consumer for at-least-once redelivery dedup. The
    /// silo-wide default; a per-consumer <c>WindowSize</c> override on
    /// <c>EdictIdempotencyBase</c> lets a singleton grain run a much larger
    /// window than the default.
    /// </summary>
    public int IdempotencyWindowSize { get; set; } = 100;

    /// <summary>The first Outbox retry delay; doubles each attempt up to <see cref="OutboxMaxDelay"/>.</summary>
    public TimeSpan OutboxBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>The Outbox backoff ceiling — a long outage cannot push the next attempt past this.</summary>
    public TimeSpan OutboxMaxDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Attempts before a permanently failing Outbox entry is promoted to a
    /// dead-letter publish. The failing attempt that reaches this
    /// count removes the entry and appends an <c>EdictDeadLetterRaised</c>
    /// publish entry at the FIFO tail in one write.
    /// </summary>
    public int OutboxMaxAttempts { get; set; } = 8;

    /// <summary>
    /// Fraction of the computed delay used as a deterministic ±spread per
    /// entry so a fleet failing together does not stampede the same retry
    /// instant. <c>0</c> disables jitter. Validated to <c>[0, 1]</c>; an
    /// out-of-range value throws at startup (no silent clamp).
    /// </summary>
    public double OutboxJitterFraction { get; set; } = 0.2;

    /// <summary>
    /// Period of the lazy Outbox drain reminder. Orleans' reminder
    /// floor is one minute; values below that throw at startup.
    /// </summary>
    public TimeSpan OutboxDrainReminderPeriod { get; set; } = TimeSpan.FromMinutes(1);
}
