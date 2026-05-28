using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using Edict.Benchmarks.Throughput.ClosedLoop;
using Edict.Benchmarks.Throughput.Saturation;

namespace Edict.Benchmarks.Throughput.Output;

/// <summary>
/// Pure-function token replacer over <c>docs/benchmarks/throughput.template.md</c>.
/// <see cref="Render"/> substitutes <c>{{token}}</c> placeholders from a token
/// dictionary plus the <c>{{table:closed_loop}}</c> markdown table built from
/// <see cref="ThroughputResults"/>. No I/O — the thin <see cref="WriteAsync"/>
/// wrapper does the file write.
/// </summary>
public static partial class MarkdownWriter
{
    public static string Render(
        string template,
        IReadOnlyDictionary<string, string> tokens,
        IReadOnlyList<ThroughputResults> results,
        IReadOnlyList<SaturationResults>? saturation = null,
        TextWriter? warningSink = null)
    {
        var saturationResults = saturation ?? [];
        var output = template.Replace("{{table:closed_loop}}", RenderClosedLoopTable(results));
        output = output.Replace("{{table:saturation}}", RenderSaturationTable(saturationResults));
        output = output.Replace("{{section:run_health}}", RenderRunHealthSection(results, saturationResults));
        foreach (var pair in tokens)
        {
            output = output.Replace("{{" + pair.Key + "}}", pair.Value);
        }
        var sink = warningSink ?? Console.Error;
        foreach (Match match in TokenPattern().Matches(output))
        {
            sink.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"MarkdownWriter: unresolved token {match.Value} left in output"));
        }
        return output;
    }

    // Closed-loop's bounded await rate-paces the producer, so EPS would invite
    // a misread as a throughput claim. The curated doc shows only latency at a
    // narrow N sweep — broader rows are preserved in CSV and dropped here.
    static readonly HashSet<int> ClosedLoopParallelisms = [2, 16, 64];
    static readonly HashSet<string> ClosedLoopScenarios = new(StringComparer.Ordinal)
    {
        "Command acceptance",
        "Command → Event delivery",
    };

    static string RenderClosedLoopTable(IReadOnlyList<ThroughputResults> results)
    {
        var sb = new StringBuilder();
        sb.Append("| Substrate | Scenario | Parallelism | p50 (ms) | p95 (ms) | p99 (ms) | Health |\n");
        sb.Append("| --- | --- | --- | ---: | ---: | ---: | :---: |");
        foreach (var r in results)
        {
            if (!ClosedLoopScenarios.Contains(r.Scenario) || !ClosedLoopParallelisms.Contains(r.Parallelism))
            {
                continue;
            }
            sb.Append('\n');
            sb.Append(CultureInfo.InvariantCulture,
                $"| {r.Substrate} | {r.Scenario} | {r.Parallelism} | {r.Latency.P50.TotalMilliseconds:F2} | {r.Latency.P95.TotalMilliseconds:F2} | {r.Latency.P99.TotalMilliseconds:F2} | {RenderHealthCell(r.Health)} |");
        }
        return sb.ToString();
    }

    static string RenderSaturationTable(IReadOnlyList<SaturationResults> results)
    {
        var sb = new StringBuilder();
        sb.Append("| Substrate | Events / sec (end-to-end) | Health |\n");
        sb.Append("| --- | ---: | :---: |");
        foreach (var r in results)
        {
            sb.Append('\n');
            sb.Append(CultureInfo.InvariantCulture,
                $"| {r.Substrate} | {r.EventsPerSecond:F0} | {RenderHealthCell(r.Health)} |");
        }
        return sb.ToString();
    }

    static string RenderHealthCell(Measurement.RunHealth health)
    {
        if (health.Attempted == 0)
        {
            return "—";
        }
        var rate = health.FailureRate;
        return health.IsHealthy
            ? string.Create(CultureInfo.InvariantCulture, $"OK ({rate:P2})")
            : string.Create(CultureInfo.InvariantCulture, $"⚠ {rate:P2}");
    }

    static string RenderRunHealthSection(
        IReadOnlyList<ThroughputResults> closedLoop,
        IReadOnlyList<SaturationResults> saturation)
    {
        var degraded = new List<string>();
        foreach (var r in closedLoop)
        {
            if (!r.Health.IsHealthy && r.Health.Attempted > 0)
            {
                degraded.Add(string.Create(CultureInfo.InvariantCulture,
                    $"- **{r.Substrate} / {r.Scenario} / N={r.Parallelism}** — {r.Health.Failed:N0} failed of {r.Health.Attempted:N0} ({r.Health.FailureRate:P2}); breakdown: {r.Health.RenderFailureTypes()}"));
            }
        }
        foreach (var r in saturation)
        {
            if (!r.Health.IsHealthy && r.Health.Attempted > 0)
            {
                degraded.Add(string.Create(CultureInfo.InvariantCulture,
                    $"- **{r.Substrate} / Saturation / N={r.ProducerConcurrency}** — {r.Health.Failed:N0} failed of {r.Health.Attempted:N0} ({r.Health.FailureRate:P2}); breakdown: {r.Health.RenderFailureTypes()}"));
            }
        }
        if (degraded.Count == 0)
        {
            return "All sweep points completed under the 1% failure-rate threshold.";
        }
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"> ⚠ {degraded.Count} sweep point(s) exceeded the 1% failure-rate threshold. The throughput numbers above are computed from successful sends only — read them against the failure breakdown below.");
        sb.AppendLine();
        foreach (var line in degraded)
        {
            sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd();
    }

    public static async Task WriteAsync(
        string path,
        string template,
        IReadOnlyDictionary<string, string> tokens,
        IReadOnlyList<ThroughputResults> results,
        IReadOnlyList<SaturationResults>? saturation = null)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await File.WriteAllTextAsync(path, Render(template, tokens, results, saturation));
    }

    [GeneratedRegex(@"\{\{[^{}]+\}\}")]
    private static partial Regex TokenPattern();
}
