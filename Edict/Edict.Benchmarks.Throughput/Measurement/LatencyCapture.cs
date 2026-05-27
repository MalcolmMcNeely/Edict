using System.Diagnostics;

namespace Edict.Benchmarks.Throughput.Measurement;

/// <summary>
/// Owns the histogram lifecycle for one closed-loop measurement window:
/// pre-size one histogram per issuer, merge across issuers for the
/// percentile rollup, and downsample the concatenated raw samples to a
/// bounded CSV row count so a reader can re-plot percentiles without
/// rerunning the harness.
/// </summary>
public static class LatencyCapture
{
    const int MaxCsvSamplesPerPoint = 10_000;

    /// <summary>
    /// Pre-size one histogram per issuer assuming up to 10k EPS per issuer
    /// with a 2x safety factor; drops on overflow stay silent in
    /// <see cref="LatencyHistogram"/>.
    /// </summary>
    public static LatencyHistogram[] CreateForIssuers(TimeSpan window, int parallelism)
    {
        var capacityPerIssuer = EstimateCapacity(window, parallelism);
        var histograms = new LatencyHistogram[parallelism];
        for (var i = 0; i < parallelism; i++)
        {
            histograms[i] = new LatencyHistogram(capacityPerIssuer);
        }
        return histograms;
    }

    public static LatencyResults MergePercentiles(LatencyHistogram[] histograms)
    {
        var total = histograms.Sum(h => h.Count);
        if (total == 0)
        {
            return new LatencyResults(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        }
        var merged = new LatencyHistogram(total);
        foreach (var histogram in histograms)
        {
            foreach (var sample in histogram.AsReadOnlySpan())
            {
                merged.Record(sample);
            }
        }
        return merged.Compute();
    }

    /// <summary>
    /// Stride-pick across the concatenated raw samples so the CSV stays
    /// bounded (~10k rows per point) but is dense enough to re-derive
    /// percentiles or plot the curve.
    /// </summary>
    public static IReadOnlyList<TimeSpan> DownsampleSamples(LatencyHistogram[] histograms)
    {
        var total = histograms.Sum(h => h.Count);
        if (total == 0)
        {
            return [];
        }
        var stride = Math.Max(1, total / MaxCsvSamplesPerPoint);
        var output = new List<TimeSpan>(Math.Min(total, MaxCsvSamplesPerPoint));
        var index = 0;
        foreach (var histogram in histograms)
        {
            var span = histogram.AsReadOnlySpan();
            for (var i = 0; i < span.Length; i++)
            {
                if (index % stride == 0)
                {
                    output.Add(TimeSpan.FromSeconds((double)span[i] / Stopwatch.Frequency));
                }
                index++;
            }
        }
        return output;
    }

    static int EstimateCapacity(TimeSpan window, int parallelism)
    {
        var perIssuer = (int)(window.TotalSeconds * 10_000 * 2);
        return Math.Max(1024, perIssuer / Math.Max(1, parallelism) + 1024);
    }
}
