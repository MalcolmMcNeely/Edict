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
        // Substrate-specific framing is hardcoded for the single registered substrate (Azurite);
        // when a second substrate lands, this block becomes a per-substrate seam.
        sb.AppendLine("## Setup");
        sb.AppendLine();
        sb.AppendLine("- Substrate: Azurite (Testcontainers, default config) — not real Azure Storage.");
        sb.AppendLine("- Single Orleans TestCluster silo (producer and consumers share one process).");
        sb.AppendLine("- Azure Queue stream provider with framework defaults; queue polling sets a hard floor on per-event latency.");
        sb.AppendLine("- Completion signal for the Events scenario is a 5 ms point-get poll against the projection table.");
        sb.AppendLine("- Single run on dev hardware; expect ±20% variance run-to-run. Numbers are a baseline for the registered substrate, not a framework ceiling.");
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
        // Group by (substrate, scenario) preserving first-seen order so the
        // markdown is stable across runs. Commands and Events scale differently
        // and live on separate metrics — collapsing them under one headline
        // would mislead a reader quoting the number.
        var order = new List<(string Substrate, string Scenario)>();
        var byKey = new Dictionary<(string, string), List<ThroughputResults>>();
        foreach (var r in results)
        {
            var key = (r.Substrate, r.Scenario);
            if (!byKey.TryGetValue(key, out var bucket))
            {
                bucket = [];
                byKey[key] = bucket;
                order.Add(key);
            }
            bucket.Add(r);
        }
        foreach (var key in order)
        {
            var peak = byKey[key].MaxBy(r => r.EventsPerSecond)!;
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"**{key.Substrate}: {peak.EventsPerSecond:F0} {peak.Scenario.ToLowerInvariant()}/sec @ N={peak.Parallelism}**");
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
