using System.Diagnostics;

using Edict.Benchmarks.Throughput;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class LatencyHistogramTests
{
    [Fact]
    public void Compute_NearestRankPercentiles_OverOneHundredEvenlySpacedSamples()
    {
        // 100 samples representing 1ms .. 100ms. Nearest-rank method:
        // p = ceil(percentile * N), so p50 → index 49 → 50ms,
        // p95 → index 94 → 95ms, p99 → index 98 → 99ms.
        var histogram = new LatencyHistogram(capacity: 100);
        for (var ms = 1; ms <= 100; ms++)
        {
            histogram.Record(MillisecondsToTicks(ms));
        }

        var results = histogram.Compute();

        Assert.Equal(50.0, results.P50.TotalMilliseconds, precision: 3);
        Assert.Equal(95.0, results.P95.TotalMilliseconds, precision: 3);
        Assert.Equal(99.0, results.P99.TotalMilliseconds, precision: 3);
    }

    static long MillisecondsToTicks(int milliseconds) =>
        (long)(milliseconds * Stopwatch.Frequency / 1000.0);
}
