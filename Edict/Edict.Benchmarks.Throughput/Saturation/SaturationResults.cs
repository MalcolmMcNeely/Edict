using Edict.Benchmarks.Throughput.Measurement;

namespace Edict.Benchmarks.Throughput.Saturation;

/// <summary>
/// Single-row outcome of a saturation pass — the count-at-window-end of
/// counter rows summed across the aggregate pool, divided by the measurement
/// window in seconds. Computed once at <c>t = window-end</c>; no per-event
/// polling, no per-send latency. EPS captures the sustained ceiling
/// <c>min(producer_rate, consumer_rate)</c> for the substrate.
/// <para>
/// <see cref="Health"/> is the producer-side outcome breakdown: under
/// fire-and-forget at N=256 the issuer loop catches every non-cancellation
/// exception (an unhandled OrleansException would otherwise crash the run
/// through <c>Task.WhenAll</c>); the count + by-type breakdown surface in
/// the CSV/markdown so the EPS number is read against the offered load
/// actually achieved, not silently undercounted.
/// </para>
/// </summary>
public sealed record SaturationResults(
    string Substrate,
    double EventsPerSecond,
    double WindowSeconds,
    int ProducerConcurrency,
    int AggregateCount,
    RunHealth Health);
