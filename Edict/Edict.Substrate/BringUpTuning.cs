namespace Edict.Substrate;

/// <summary>
/// Lowered, env-overridable timing knobs for substrate bring-up. The throughput
/// bench brings fresh containers up roughly five times per substrate per run, so
/// the stock Testcontainers waits (a silent one-hour built-in wait, a 60 s
/// host-readiness probe) turn a stalled gvproxy port mapping into an unbounded
/// hang. These defaults fail fast into a fresh-container retry — the only thing
/// that clears a stalled mapping — while staying tunable for a genuinely-slow
/// laptop boot. Reads environment only; no other I/O.
/// </summary>
public sealed record BringUpTuning
{
    public const string TestcontainersWaitSecondsVariable = "EDICT_BENCH_TESTCONTAINERS_WAIT_SECONDS";
    public const string HostProbeDeadlineSecondsVariable = "EDICT_BENCH_HOST_PROBE_DEADLINE_SECONDS";
    public const string HostProbePollMillisecondsVariable = "EDICT_BENCH_HOST_PROBE_POLL_MS";
    public const string WithinBootStaggerSecondsVariable = "EDICT_BENCH_BRINGUP_STAGGER_SECONDS";
    public const string CrossBootSettleSecondsVariable = "EDICT_BENCH_BRINGUP_SETTLE_SECONDS";
    public const string RetryCountVariable = "EDICT_BENCH_BRINGUP_RETRIES";
    public const string SetupDeadlineSecondsVariable = "EDICT_BENCH_SETUP_DEADLINE_SECONDS";

    /// <summary>
    /// Timeout handed to the Testcontainers built-in wait strategy, replacing its
    /// ~1 hour default so an in-container readiness hang cannot eat the whole
    /// setup deadline on the first attempt.
    /// </summary>
    public TimeSpan TestcontainersWaitTimeout { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Deadline for the host-side readiness probe (host TCP connect to the
    /// container's mapped ports), lowered from 60 s so a stalled mapping fails
    /// fast into a fresh-container retry.
    /// </summary>
    public TimeSpan HostReadinessProbeDeadline { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Poll cadence the host-readiness probe retries its connect on.</summary>
    public TimeSpan HostReadinessProbePollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Gap between successive within-boot steps, so a multi-container substrate's
    /// heavy starts do not fight for CPU and disk simultaneously.
    /// </summary>
    public TimeSpan WithinBootStaggerGap { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Settle gap between a pass's container teardown and the next pass's
    /// bring-up, giving the gvproxy port-forwarder a moment to drain. Applied by
    /// the harness across boots, not by the per-boot policy.
    /// </summary>
    public TimeSpan CrossBootSettleGap { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Number of fresh-container bring-up attempts before giving up.</summary>
    public int RetryCount { get; init; } = 3;

    /// <summary>
    /// Wall-clock budget for the whole setup phase (container bring-up plus silo
    /// deploy). Applied by the harness, not by the per-boot policy.
    /// </summary>
    public TimeSpan SetupDeadline { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Reads the tuning from environment variables, falling back to the lowered
    /// defaults for any variable that is absent or malformed (a typo in a tuning
    /// knob must not crash the bench). The <paramref name="readEnvironmentVariable"/>
    /// seam lets tests drive parsing without mutating real process environment.
    /// </summary>
    public static BringUpTuning FromEnvironment(Func<string, string?>? readEnvironmentVariable = null)
    {
        var read = readEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        var defaults = new BringUpTuning();
        return new BringUpTuning
        {
            TestcontainersWaitTimeout = ReadSeconds(read, TestcontainersWaitSecondsVariable, defaults.TestcontainersWaitTimeout),
            HostReadinessProbeDeadline = ReadSeconds(read, HostProbeDeadlineSecondsVariable, defaults.HostReadinessProbeDeadline),
            HostReadinessProbePollInterval = ReadMilliseconds(read, HostProbePollMillisecondsVariable, defaults.HostReadinessProbePollInterval),
            WithinBootStaggerGap = ReadSeconds(read, WithinBootStaggerSecondsVariable, defaults.WithinBootStaggerGap),
            CrossBootSettleGap = ReadSeconds(read, CrossBootSettleSecondsVariable, defaults.CrossBootSettleGap),
            RetryCount = ReadPositiveInt(read, RetryCountVariable, defaults.RetryCount),
            SetupDeadline = ReadSeconds(read, SetupDeadlineSecondsVariable, defaults.SetupDeadline),
        };
    }

    static TimeSpan ReadSeconds(Func<string, string?> read, string variable, TimeSpan fallback) =>
        TryReadPositiveInt(read, variable, out var seconds) ? TimeSpan.FromSeconds(seconds) : fallback;

    static TimeSpan ReadMilliseconds(Func<string, string?> read, string variable, TimeSpan fallback) =>
        TryReadPositiveInt(read, variable, out var milliseconds) ? TimeSpan.FromMilliseconds(milliseconds) : fallback;

    static int ReadPositiveInt(Func<string, string?> read, string variable, int fallback) =>
        TryReadPositiveInt(read, variable, out var value) ? value : fallback;

    static bool TryReadPositiveInt(Func<string, string?> read, string variable, out int value)
    {
        value = 0;
        var raw = read(variable);
        return !string.IsNullOrWhiteSpace(raw)
            && int.TryParse(raw, out value)
            && value > 0;
    }
}
