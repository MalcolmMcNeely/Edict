using System.Diagnostics;

namespace Edict.Benchmarks.Throughput;

/// <summary>
/// Fixed-capacity recorder for stopwatch-tick latency samples; computes
/// p50/p95/p99 by sorted-index nearest-rank lookup. The harness presizes one
/// histogram per issuer and merges at end of measurement, so Record is on the
/// hot path — kept allocation-free.
/// </summary>
public sealed class LatencyHistogram
{
    readonly long[] _samples;
    int _count;

    public LatencyHistogram(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _samples = new long[capacity];
    }

    public int Count => _count;

    public int Capacity => _samples.Length;

    public ReadOnlySpan<long> AsReadOnlySpan() => _samples.AsSpan(0, _count);

    /// <summary>
    /// Appends one sample. Overflow is dropped silently — the harness sizes
    /// capacity from <c>warmup+window × expected EPS × safety factor</c>;
    /// dropping on overflow is preferable to allocating mid-measurement.
    /// </summary>
    public void Record(long stopwatchTicks)
    {
        if (_count < _samples.Length)
        {
            _samples[_count++] = stopwatchTicks;
        }
    }

    public LatencyResults Compute()
    {
        if (_count == 0)
        {
            return new LatencyResults(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        }

        var sorted = new long[_count];
        Array.Copy(_samples, sorted, _count);
        Array.Sort(sorted);

        return new LatencyResults(
            TicksToTimeSpan(NearestRank(sorted, 0.50)),
            TicksToTimeSpan(NearestRank(sorted, 0.95)),
            TicksToTimeSpan(NearestRank(sorted, 0.99)));
    }

    static long NearestRank(long[] sorted, double percentile)
    {
        var rank = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        if (rank < 0)
        {
            rank = 0;
        }
        else if (rank >= sorted.Length)
        {
            rank = sorted.Length - 1;
        }
        return sorted[rank];
    }

    static TimeSpan TicksToTimeSpan(long stopwatchTicks) =>
        TimeSpan.FromSeconds((double)stopwatchTicks / Stopwatch.Frequency);
}

public readonly record struct LatencyResults(TimeSpan P50, TimeSpan P95, TimeSpan P99);
