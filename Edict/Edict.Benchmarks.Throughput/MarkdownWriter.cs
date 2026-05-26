using System.Globalization;
using System.Text;

namespace Edict.Benchmarks.Throughput;

/// <summary>
/// Renders <see cref="ThroughputResults"/> into the committed
/// <c>docs/benchmarks/throughput.md</c> table. One row per
/// (substrate, scenario, parallelism); header block declares the machine
/// class, .NET version, run date and git SHA so a reader can judge the numbers.
/// </summary>
public static class MarkdownWriter
{
    public static string Render(
        IReadOnlyList<ThroughputResults> results,
        DateTimeOffset runDate,
        RunMetadata metadata)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Edict throughput");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Machine: {metadata.MachineClass}");
        sb.AppendLine(CultureInfo.InvariantCulture, $".NET version: {metadata.DotnetVersion}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Run date: {runDate:yyyy-MM-dd}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Git SHA: {metadata.GitSha}");
        sb.AppendLine();
        foreach (var headline in PeakHeadlines(results))
        {
            sb.AppendLine(headline);
        }
        if (results.Count > 0)
        {
            sb.AppendLine();
        }
        sb.AppendLine("| Substrate | Scenario | Parallelism | EPS | p50 (ms) | p95 (ms) | p99 (ms) |");
        sb.AppendLine("| --- | --- | --- | ---: | ---: | ---: | ---: |");
        foreach (var r in results)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"| {r.Substrate} | {r.Scenario} | {r.Parallelism} | {r.EventsPerSecond:F0} | {r.Latency.P50.TotalMilliseconds:F2} | {r.Latency.P95.TotalMilliseconds:F2} | {r.Latency.P99.TotalMilliseconds:F2} |");
        }
        return sb.ToString();
    }

    static IEnumerable<string> PeakHeadlines(IReadOnlyList<ThroughputResults> results)
    {
        // Group preserving first-seen substrate order so the markdown is stable
        // across runs regardless of how the sweep was scheduled.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<string>();
        var byName = new Dictionary<string, List<ThroughputResults>>(StringComparer.Ordinal);
        foreach (var r in results)
        {
            if (seen.Add(r.Substrate))
            {
                order.Add(r.Substrate);
                byName[r.Substrate] = [];
            }
            byName[r.Substrate].Add(r);
        }
        foreach (var name in order)
        {
            var peak = byName[name].MaxBy(r => r.EventsPerSecond)!;
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"**{name}: {peak.EventsPerSecond:F0} {peak.Scenario.ToLowerInvariant()}/sec @ N={peak.Parallelism}**");
        }
    }

    public static Task WriteAsync(
        string path,
        IReadOnlyList<ThroughputResults> results,
        DateTimeOffset runDate,
        RunMetadata metadata)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        return File.WriteAllTextAsync(path, Render(results, runDate, metadata));
    }
}
