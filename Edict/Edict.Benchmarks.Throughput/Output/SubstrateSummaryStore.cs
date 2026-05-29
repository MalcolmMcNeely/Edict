using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

using Edict.Benchmarks.Throughput.ClosedLoop;
using Edict.Benchmarks.Throughput.Measurement;
using Edict.Benchmarks.Throughput.Saturation;

namespace Edict.Benchmarks.Throughput.Output;

/// <summary>
/// File-backed store for <see cref="SubstrateSummary"/>. Each substrate's
/// summary lives at <c>raw/&lt;substrate&gt;-summary.json</c>; a run
/// overwrites only its own substrate file, so a single-substrate invocation
/// preserves every other substrate's row in the rendered markdown.
/// </summary>
public static class SubstrateSummaryStore
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string PathFor(string rawDirectory, string substrateName) =>
        Path.Combine(rawDirectory, $"{substrateName}-summary.json");

    public static SubstrateSummary BuildFromResults(
        string substrate,
        DateTimeOffset runDate,
        IReadOnlyList<ThroughputResults> closedLoop,
        SaturationResults? saturation)
    {
        var rows = new List<ClosedLoopSummaryRow>();
        foreach (var r in closedLoop)
        {
            if (!string.Equals(r.Substrate, substrate, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            rows.Add(new ClosedLoopSummaryRow
            {
                Scenario = r.Scenario,
                Parallelism = r.Parallelism,
                CompletedCount = r.CompletedCount,
                ElapsedSeconds = r.ElapsedMeasurement.TotalSeconds,
                P50Ms = r.Latency.P50.TotalMilliseconds,
                P95Ms = r.Latency.P95.TotalMilliseconds,
                P99Ms = r.Latency.P99.TotalMilliseconds,
                Health = ToSummary(r.Health),
            });
        }

        SaturationSummaryRow? saturationRow = null;
        if (saturation is not null
            && string.Equals(saturation.Substrate, substrate, StringComparison.OrdinalIgnoreCase))
        {
            saturationRow = new SaturationSummaryRow
            {
                EventsPerSecond = saturation.EventsPerSecond,
                WindowSeconds = saturation.WindowSeconds,
                ProducerConcurrency = saturation.ProducerConcurrency,
                AggregateCount = saturation.AggregateCount,
                Health = ToSummary(saturation.Health),
            };
        }

        return new SubstrateSummary
        {
            Substrate = substrate,
            RunDate = runDate,
            ClosedLoop = rows,
            Saturation = saturationRow,
        };
    }

    public static async Task WriteAsync(string path, SubstrateSummary summary)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, summary, Options);
    }

    public static async Task<SubstrateSummary?> ReadAsync(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SubstrateSummary>(stream, Options);
    }

    /// <summary>
    /// Reads every <c>&lt;substrate&gt;-summary.json</c> in <paramref name="rawDirectory"/>.
    /// Returns an empty list when the directory does not exist so a first-ever
    /// run is a clean no-op rather than a thrown exception.
    /// </summary>
    public static async Task<IReadOnlyList<SubstrateSummary>> ReadAllAsync(string rawDirectory)
    {
        if (!Directory.Exists(rawDirectory))
        {
            return [];
        }
        var summaries = new List<SubstrateSummary>();
        foreach (var file in Directory.EnumerateFiles(rawDirectory, "*-summary.json"))
        {
            var summary = await ReadAsync(file);
            if (summary is not null && !string.IsNullOrWhiteSpace(summary.Substrate))
            {
                summaries.Add(summary);
            }
        }
        return summaries;
    }

    /// <summary>
    /// Expands the per-substrate summary records back into the shapes
    /// <see cref="MarkdownWriter"/> already renders from — same closed-loop /
    /// saturation row types as a live run produces. <c>runDates</c> keys each
    /// substrate's <c>{{run_date:&lt;substrate&gt;}}</c> token.
    /// </summary>
    public static HydratedSummaries Hydrate(IReadOnlyList<SubstrateSummary> summaries)
    {
        var closedLoop = new List<ThroughputResults>();
        var saturation = new List<SaturationResults>();
        var runDates = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in summaries)
        {
            runDates[s.Substrate] = s.RunDate;
            foreach (var row in s.ClosedLoop)
            {
                closedLoop.Add(new ThroughputResults(
                    Substrate: s.Substrate,
                    Scenario: row.Scenario,
                    Parallelism: row.Parallelism,
                    CompletedCount: row.CompletedCount,
                    ElapsedMeasurement: TimeSpan.FromSeconds(row.ElapsedSeconds),
                    Latency: new LatencyResults(
                        P50: TimeSpan.FromMilliseconds(row.P50Ms),
                        P95: TimeSpan.FromMilliseconds(row.P95Ms),
                        P99: TimeSpan.FromMilliseconds(row.P99Ms)),
                    Health: FromSummary(row.Health)));
            }
            if (s.Saturation is not null)
            {
                saturation.Add(new SaturationResults(
                    Substrate: s.Substrate,
                    EventsPerSecond: s.Saturation.EventsPerSecond,
                    WindowSeconds: s.Saturation.WindowSeconds,
                    ProducerConcurrency: s.Saturation.ProducerConcurrency,
                    AggregateCount: s.Saturation.AggregateCount,
                    Health: FromSummary(s.Saturation.Health)));
            }
        }
        return new HydratedSummaries(closedLoop, saturation, runDates);
    }

    static RunHealthSummary ToSummary(RunHealth health) => new()
    {
        Succeeded = health.Succeeded,
        Failed = health.Failed,
        FailuresByType = health.FailuresByType.ToDictionary(
            kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal),
    };

    static RunHealth FromSummary(RunHealthSummary summary) => new(
        Succeeded: summary.Succeeded,
        Failed: summary.Failed,
        FailuresByType: summary.FailuresByType
            .ToImmutableSortedDictionary(kvp => kvp.Key, kvp => kvp.Value));
}

public sealed record HydratedSummaries(
    IReadOnlyList<ThroughputResults> ClosedLoop,
    IReadOnlyList<SaturationResults> Saturation,
    IReadOnlyDictionary<string, DateTimeOffset> RunDates);
