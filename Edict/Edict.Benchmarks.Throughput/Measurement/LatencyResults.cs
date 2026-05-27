namespace Edict.Benchmarks.Throughput.Measurement;

public readonly record struct LatencyResults(TimeSpan P50, TimeSpan P95, TimeSpan P99);
