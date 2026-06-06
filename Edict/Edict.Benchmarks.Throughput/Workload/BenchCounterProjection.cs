using Edict.Contracts.Persistence;

namespace Edict.Benchmarks.Throughput.Workload;

/// <summary>
/// In-grain counter payload written by <see cref="BenchCounterStateProjectionBuilder"/>
/// in the State-species saturation pass. The mirror of <see cref="BenchCounterRow"/>,
/// but kept in the grain's own durable state and committed inline with the dedup
/// ring (no external store, no outbox effect) — the storage difference the
/// side-by-side saturation table measures. One projection grain per aggregate
/// (keyed by the event's <c>[EdictRouteKey]</c>); the harness's State pass sums
/// <see cref="Count"/> across the 1024-aggregate pool through the projection reader
/// at <c>t = window-end</c>.
/// </summary>
[GenerateSerializer]
[Alias("Edict.Benchmarks.Throughput.Workload.BenchCounterProjection")]
public sealed class BenchCounterProjection : IEdictPersistedState
{
    [Id(0)]
    public long Count { get; set; }
}
