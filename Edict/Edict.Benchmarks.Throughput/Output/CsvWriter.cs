using System.Globalization;
using System.Text;

using Edict.Benchmarks.Throughput.ClosedLoop;

namespace Edict.Benchmarks.Throughput.Output;

/// <summary>
/// Writes a long-format raw-sample CSV per substrate to
/// <c>docs/benchmarks/raw/&lt;date&gt;-&lt;substrate&gt;.csv</c>. One row per
/// latency sample so a reader can re-plot percentiles or rebuild the curve
/// without rerunning the harness.
/// </summary>
public static class CsvWriter
{
    public static string Render(IReadOnlyList<ThroughputResults> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("substrate,scenario,parallelism,events_per_second,succeeded,failed,failure_rate,failure_types,latency_ms");
        foreach (var r in results)
        {
            // Group-constant columns repeated per latency-sample row keep the
            // long format pivot-friendly: a reader can group by (substrate,
            // scenario, parallelism) and the health columns are stable inside
            // each group, so failure-rate joins cleanly against EPS without
            // a separate summary table.
            var succeeded = r.Health.Succeeded;
            var failed = r.Health.Failed;
            var failureRate = r.Health.FailureRate;
            var failureTypes = r.Health.RenderFailureTypes();
            foreach (var sample in r.LatencySamples)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"{r.Substrate},{r.Scenario},{r.Parallelism},{r.EventsPerSecond:F2},{succeeded},{failed},{failureRate:F4},{failureTypes},{sample.TotalMilliseconds:F3}");
            }
        }
        return sb.ToString();
    }

    public static Task WriteAsync(string path, IReadOnlyList<ThroughputResults> results)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        return File.WriteAllTextAsync(path, Render(results));
    }
}
