using Edict.Contracts.Events;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

namespace Edict.Benchmarks.Throughput.Workload;

/// <summary>
/// Per-aggregate counter for the saturation pass. Reads
/// <c>(aggregateId, FixedRowKey)</c>, increments <see cref="BenchCounterRow.Count"/>,
/// writes back — Orleans grain serialisation per aggregate eliminates ETag
/// contention, so the throughput ceiling is the substrate's per-aggregate
/// consumer rate, not optimistic-concurrency retries.
/// </summary>
public sealed partial class BenchCounterProjectionBuilder : EdictTableProjectionBuilder<BenchCounterRow>
{
    public const string TableNameLiteral = "benchcounter";
    public const string FixedRowKey = "counter";

    public BenchCounterProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => TableNameLiteral;

    protected override string GetRowKey(EdictEvent edictEvent) => FixedRowKey;

    public Task Handle(BenchEvent edictEvent)
    {
        CurrentRow.Count++;
        return Task.CompletedTask;
    }
}
