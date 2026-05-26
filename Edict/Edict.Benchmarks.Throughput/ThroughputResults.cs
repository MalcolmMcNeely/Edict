namespace Edict.Benchmarks.Throughput;

public sealed record ThroughputResults(
    string Substrate,
    string Scenario,
    int Parallelism,
    long CompletedCount,
    TimeSpan ElapsedMeasurement,
    LatencyResults Latency)
{
    public double EventsPerSecond =>
        ElapsedMeasurement.TotalSeconds > 0
            ? CompletedCount / ElapsedMeasurement.TotalSeconds
            : 0;
}
