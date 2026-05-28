using System.Globalization;
using System.Text;

using Edict.Benchmarks.Throughput.Saturation;

namespace Edict.Benchmarks.Throughput.Output;

/// <summary>
/// Writes one row per <see cref="SaturationResults"/> to
/// <c>docs/benchmarks/raw/&lt;date&gt;-&lt;substrate&gt;-saturation.csv</c>.
/// Distinct from <see cref="CsvWriter"/>'s long-format latency-sample CSV — the
/// saturation pass computes EPS once at <c>t = window-end</c>, so there is one
/// row per (substrate, run).
/// </summary>
public static class SaturationCsvWriter
{
    public static string Render(IReadOnlyList<SaturationResults> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("substrate,events_per_second,window_seconds,producer_concurrency,aggregate_count,succeeded,failed,failure_rate,failure_types");
        foreach (var r in results)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"{r.Substrate},{r.EventsPerSecond:F2},{r.WindowSeconds:F2},{r.ProducerConcurrency},{r.AggregateCount},{r.Health.Succeeded},{r.Health.Failed},{r.Health.FailureRate:F4},{r.Health.RenderFailureTypes()}");
        }
        return sb.ToString();
    }

    public static Task WriteAsync(string path, IReadOnlyList<SaturationResults> results)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        return File.WriteAllTextAsync(path, Render(results));
    }
}
