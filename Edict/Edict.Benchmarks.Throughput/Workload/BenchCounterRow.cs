using Edict.Contracts.Persistence;

namespace Edict.Benchmarks.Throughput.Workload;

/// <summary>
/// Counter-per-aggregate projection row written by
/// <see cref="BenchCounterProjectionBuilder"/> in the saturation pass.
/// PK is the aggregate ID (the event's <c>[EdictRouteKey]</c>); RK is a fixed
/// sentinel so each aggregate has exactly one counter row that grows under
/// load. The harness's saturation read sums <see cref="Count"/> across the
/// 1024-aggregate pool at <c>t = window-end</c>.
/// </summary>
[GenerateSerializer]
[Alias("Edict.Benchmarks.Throughput.Workload.BenchCounterRow")]
public sealed class BenchCounterRow : IEdictPersistedState
{
    [Id(0)]
    public long Count { get; set; }
}
