namespace Edict.Benchmarks.Throughput.ClosedLoop;

/// <summary>
/// One closed-loop test case — a self-contained issuer body that fires a
/// single round-trip against the running cluster and returns once the
/// scenario's completion condition is met. The runner times each
/// <see cref="IssueOnceAsync"/> call and feeds the elapsed ticks into a
/// per-issuer <c>LatencyHistogram</c>. New scenarios are new files, not
/// enum cases — the implementations sitting next to this interface are the
/// "what is being measured" surface for the project.
/// </summary>
public interface IClosedLoopScenario
{
    /// <summary>
    /// Stable, CSV-bound name (mirrors the historical <c>Scenario.ToString()</c>
    /// values: <c>Commands</c>, <c>RaiseOnly</c>, <c>Events</c>). Persisted
    /// into <see cref="ThroughputResults.Scenario"/> and the long-format CSV.
    /// </summary>
    string Name { get; }

    Task IssueOnceAsync(Guid aggregateId, byte[] filler, CancellationToken ct);
}
