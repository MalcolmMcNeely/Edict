namespace Edict.Benchmarks.Throughput.Output;

/// <summary>
/// Per-substrate state file written alongside the raw CSVs and read at
/// markdown-render time. One file per substrate (no date prefix — always
/// latest) means a single-substrate run refreshes only that file, and the
/// renderer can union all summaries on disk so the other substrate's rows
/// stay in the published markdown.
/// </summary>
public sealed record SubstrateSummary
{
    public string Substrate { get; init; } = "";

    public DateTimeOffset RunDate { get; init; }

    public IReadOnlyList<ClosedLoopSummaryRow> ClosedLoop { get; init; } = [];

    public SaturationSummaryRow? Saturation { get; init; }
}

public sealed record ClosedLoopSummaryRow
{
    public string Scenario { get; init; } = "";

    public int Parallelism { get; init; }

    public long CompletedCount { get; init; }

    public double ElapsedSeconds { get; init; }

    public double P50Ms { get; init; }

    public double P95Ms { get; init; }

    public double P99Ms { get; init; }

    public RunHealthSummary Health { get; init; } = new();
}

public sealed record SaturationSummaryRow
{
    public double EventsPerSecond { get; init; }

    public double WindowSeconds { get; init; }

    public int ProducerConcurrency { get; init; }

    public int AggregateCount { get; init; }

    public RunHealthSummary Health { get; init; } = new();
}

public sealed record RunHealthSummary
{
    public long Succeeded { get; init; }

    public long Failed { get; init; }

    public IReadOnlyDictionary<string, long> FailuresByType { get; init; } =
        new Dictionary<string, long>(StringComparer.Ordinal);
}
