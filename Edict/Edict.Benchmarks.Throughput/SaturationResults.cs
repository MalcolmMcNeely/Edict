namespace Edict.Benchmarks.Throughput;

/// <summary>
/// Single-row outcome of a saturation pass — the count-at-window-end of
/// counter rows summed across the aggregate pool, divided by the measurement
/// window in seconds. Computed once at <c>t = window-end</c>; no per-event
/// polling, no per-send latency. EPS captures the sustained ceiling
/// <c>min(producer_rate, consumer_rate)</c> for the substrate.
/// </summary>
public sealed record SaturationResults(
    string Substrate,
    double EventsPerSecond,
    double WindowSeconds,
    int ProducerConcurrency,
    int AggregateCount);
