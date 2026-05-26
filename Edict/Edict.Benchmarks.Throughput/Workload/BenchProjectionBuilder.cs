using Edict.Contracts.Events;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

namespace Edict.Benchmarks.Throughput.Workload;

/// <summary>
/// Writes one empty <see cref="BenchEventRow"/> per <see cref="BenchEvent"/>,
/// partition-keyed by the grain's primary key (the event's RouteKey) and
/// row-keyed by a fixed literal — so the issuer's completion poll is a
/// point-get on a known pk/rk pair.
/// </summary>
public sealed partial class BenchProjectionBuilder : EdictTableProjectionBuilder<BenchEventRow>
{
    public const string TableNameLiteral = "benchevent";
    public const string FixedRowKey = "bench";

    public BenchProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => TableNameLiteral;

    protected override string GetRowKey(EdictEvent evt) => FixedRowKey;

    public Task Handle(BenchEvent evt) => Task.CompletedTask;
}
