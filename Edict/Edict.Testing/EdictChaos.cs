using System.Globalization;

namespace Edict.Testing;

/// <summary>
/// The process-wide chaos seed for a test run. Resolved once on first read and
/// memoised, so every <see cref="EdictTestApp"/> in the run builds its independent
/// <see cref="Random"/> streams from the same value: per-test determinism is intact
/// and the whole run is reproducible from one number. The seed is taken from the
/// <c>EDICT_CHAOS_SEED</c> environment variable when set (both <c>0x</c>-prefixed
/// hex and decimal parse), otherwise one random <see cref="int"/> is generated.
/// On first resolution the seed is written to standard output, so a failing run's
/// log always carries it; copy the printed value into <c>EDICT_CHAOS_SEED</c> to
/// pin the whole run to that interleaving and reproduce the failure.
/// </summary>
public static class EdictChaos
{
    const string SeedEnvironmentVariable = "EDICT_CHAOS_SEED";

    static readonly Lock ResolutionLock = new();
    static int? _resolvedSeed;

    /// <summary>
    /// The seed for this run, resolved on first read and stable thereafter.
    /// </summary>
    public static int CurrentSeed
    {
        get
        {
            lock (ResolutionLock)
            {
                if (_resolvedSeed is { } already)
                {
                    return already;
                }

                var resolved = Resolve();
                _resolvedSeed = resolved;
                Console.Out.WriteLine($"[Edict] Chaos seed for this run is {resolved} (0x{resolved:X}). Reproduce with EDICT_CHAOS_SEED={resolved}.");
                return resolved;
            }
        }
    }

    static int Resolve()
    {
        var raw = Environment.GetEnvironmentVariable(SeedEnvironmentVariable);
        if (raw is not null && TryParseSeed(raw, out var parsed))
        {
            return parsed;
        }

        return Random.Shared.Next();
    }

    static bool TryParseSeed(string raw, out int seed)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(trimmed.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out seed);
        }

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed);
    }

    // The line a harness-owned throw appends so a developer can reproduce the
    // failing interleaving without hunting for the surfaced stdout line.
    internal static string ReproduceInstruction =>
        $"Chaos seed for this run was {CurrentSeed}. Reproduce with EDICT_CHAOS_SEED={CurrentSeed}.";

    // Lets the isolation and reproduction tests re-resolve the seed under a scoped
    // environment value; never called on a production run, where the seed resolves
    // exactly once.
    internal static void ResetMemoizedSeedForTesting()
    {
        lock (ResolutionLock)
        {
            _resolvedSeed = null;
        }
    }
}
