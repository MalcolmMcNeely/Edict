using Edict.Contracts.Persistence;

namespace Edict.Benchmarks.Throughput.Workload;

[GenerateSerializer]
[Alias("Edict.Benchmarks.Throughput.Workload.BenchAggregateState")]
public sealed class BenchAggregateState : IEdictPersistedState
{
    [Id(0)]
    public int Count { get; set; }
}
