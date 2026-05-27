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
/// <para>
/// Note: this projection ships alongside <see cref="BenchProjectionBuilder"/> in
/// the same assembly. Orleans auto-discovers both grains, so closed-loop and
/// saturation clusters technically run both projections. Substrate work doubles
/// per event; numbers should be read as "consumer rate with one row-per-event +
/// one counter projection active" until a follow-up adds a per-cluster
/// grain-discovery filter.
/// </para>
/// </summary>
public sealed partial class BenchCounterProjectionBuilder : EdictTableProjectionBuilder<BenchCounterRow>
{
    public const string TableNameLiteral = "benchcounter";
    public const string FixedRowKey = "counter";

    public BenchCounterProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => TableNameLiteral;

    protected override string GetRowKey(EdictEvent evt) => FixedRowKey;

    public Task Handle(BenchEvent evt)
    {
        CurrentRow.Count++;
        return Task.CompletedTask;
    }
}
