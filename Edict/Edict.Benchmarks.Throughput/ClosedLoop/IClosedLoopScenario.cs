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
    /// Stable, CSV-bound name (e.g. <c>Command acceptance</c>,
    /// <c>Command → Event delivery</c>). Persisted into
    /// <see cref="ThroughputResults.Scenario"/> and the long-format CSV; the
    /// curated markdown's closed-loop section filters on these values.
    /// </summary>
    string Name { get; }

    Task IssueOnceAsync(Guid aggregateId, byte[] filler, CancellationToken cancellationToken);
}
