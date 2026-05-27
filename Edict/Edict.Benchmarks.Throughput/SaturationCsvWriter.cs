using System.Globalization;
using System.Text;

namespace Edict.Benchmarks.Throughput;

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
        sb.AppendLine("substrate,events_per_second,window_seconds,producer_concurrency,aggregate_count");
        foreach (var r in results)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"{r.Substrate},{r.EventsPerSecond:F2},{r.WindowSeconds:F2},{r.ProducerConcurrency},{r.AggregateCount}");
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
