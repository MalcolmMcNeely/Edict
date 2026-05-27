using System.Globalization;
using System.Text;

using Edict.Benchmarks.Throughput.ClosedLoop;

namespace Edict.Benchmarks.Throughput.Output;

/// <summary>
/// Writes a long-format raw-sample CSV per substrate to
/// <c>docs/benchmarks/raw/&lt;date&gt;-&lt;substrate&gt;.csv</c>. One row per
/// latency sample so a reader can re-plot percentiles or rebuild the curve
/// without rerunning the harness (issue #126).
/// </summary>
public static class CsvWriter
{
    public static string Render(IReadOnlyList<ThroughputResults> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("substrate,scenario,parallelism,latency_ms");
        foreach (var r in results)
        {
            foreach (var sample in r.LatencySamples)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"{r.Substrate},{r.Scenario},{r.Parallelism},{sample.TotalMilliseconds:F3}");
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
