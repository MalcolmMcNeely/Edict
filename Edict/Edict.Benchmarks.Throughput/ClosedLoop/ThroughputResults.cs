using Edict.Benchmarks.Throughput.Measurement;

namespace Edict.Benchmarks.Throughput.ClosedLoop;

public sealed record ThroughputResults(
    string Substrate,
    string Scenario,
    int Parallelism,
    long CompletedCount,
    TimeSpan ElapsedMeasurement,
    LatencyResults Latency,
    RunHealth Health)
{
    public double EventsPerSecond =>
        ElapsedMeasurement.TotalSeconds > 0
            ? CompletedCount / ElapsedMeasurement.TotalSeconds
            : 0;

    /// <summary>
    /// Raw per-request latency samples from the measurement window. Surfaced
    /// so the CSV writer can emit long-format rows a reader can re-plot
    /// (issue #126). Empty by default — the runner attaches samples for the
    /// publishable sweep path; ad-hoc constructors don't have to.
    /// </summary>
    public IReadOnlyList<TimeSpan> LatencySamples { get; init; } = [];
}
