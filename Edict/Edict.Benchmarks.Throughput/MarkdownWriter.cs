using System.Globalization;
using System.Text;

namespace Edict.Benchmarks.Throughput;

/// <summary>
/// Renders <see cref="ThroughputResults"/> into the committed
/// <c>docs/benchmarks/throughput.md</c> table. One row per
/// (substrate, scenario, parallelism) — the same shape the future sweep harness
/// will emit, just with one row today.
/// </summary>
public static class MarkdownWriter
{
    public static string Render(
        IReadOnlyList<ThroughputResults> results,
        DateTimeOffset runDate)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Edict throughput");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Run date: {runDate:yyyy-MM-dd}");
        sb.AppendLine(CultureInfo.InvariantCulture, $".NET version: {Environment.Version}");
        sb.AppendLine();
        sb.AppendLine("| Substrate | Scenario | Parallelism | EPS | p50 (ms) | p95 (ms) | p99 (ms) |");
        sb.AppendLine("| --- | --- | --- | ---: | ---: | ---: | ---: |");
        foreach (var r in results)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {r.Substrate} | {r.Scenario} | {r.Parallelism} | {r.EventsPerSecond:F0} | {r.Latency.P50.TotalMilliseconds:F2} | {r.Latency.P95.TotalMilliseconds:F2} | {r.Latency.P99.TotalMilliseconds:F2} |");
        }
        return sb.ToString();
    }

    public static Task WriteAsync(
        string path,
        IReadOnlyList<ThroughputResults> results,
        DateTimeOffset runDate)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        return File.WriteAllTextAsync(path, Render(results, runDate));
    }
}
