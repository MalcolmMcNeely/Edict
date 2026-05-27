using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Edict.Benchmarks.Throughput;

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
        TextWriter? warningSink = null)
    {
        var output = template.Replace("{{table:closed_loop}}", RenderClosedLoopTable(results));
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

    static string RenderClosedLoopTable(IReadOnlyList<ThroughputResults> results)
    {
        var sb = new StringBuilder();
        sb.Append("| Substrate | Scenario | Parallelism | Events per second (EPS) | p50 (ms) | p95 (ms) | p99 (ms) |\n");
        sb.Append("| --- | --- | --- | ---: | ---: | ---: | ---: |");
        foreach (var r in results)
        {
            sb.Append('\n');
            sb.Append(CultureInfo.InvariantCulture,
                $"| {r.Substrate} | {r.Scenario} | {r.Parallelism} | {r.EventsPerSecond:F0} | {r.Latency.P50.TotalMilliseconds:F2} | {r.Latency.P95.TotalMilliseconds:F2} | {r.Latency.P99.TotalMilliseconds:F2} |");
        }
        return sb.ToString();
    }

    public static async Task WriteAsync(
        string path,
        string template,
        IReadOnlyDictionary<string, string> tokens,
        IReadOnlyList<ThroughputResults> results)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await File.WriteAllTextAsync(path, Render(template, tokens, results));
    }

    [GeneratedRegex(@"\{\{[^{}]+\}\}")]
    private static partial Regex TokenPattern();
}
