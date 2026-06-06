using Edict.Contracts.Events;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

namespace Edict.Benchmarks.Throughput.Workload;

/// <summary>
/// Per-aggregate counter for the List-species saturation pass. Reads
/// <c>(aggregateId, FixedRowKey)</c>, increments <see cref="BenchCounterRow.Count"/>,
/// writes back to the external keyed store — the at-least-once <c>UpsertRow</c>
/// drain whose commit cost the side-by-side saturation table isolates against the
/// in-grain <see cref="BenchCounterStateProjectionBuilder"/>. Orleans grain
/// serialisation per aggregate eliminates ETag contention, so the throughput
/// ceiling is the substrate's per-aggregate consumer rate, not
/// optimistic-concurrency retries.
/// </summary>
public sealed partial class BenchCounterListProjectionBuilder : EdictListProjectionBuilder<BenchCounterRow>
{
    public const string TableNameLiteral = "benchcounter";
    public const string FixedRowKey = "counter";

    public BenchCounterListProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => TableNameLiteral;

    protected override string GetRowKey(EdictEvent edictEvent) => FixedRowKey;

    Task HandleAsync(BenchEvent edictEvent)
    {
        CurrentRow.Count++;
        return Task.CompletedTask;
    }
}
