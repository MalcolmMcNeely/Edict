using Edict.Contracts.Events;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

namespace Edict.Benchmarks.Throughput.Workload;

/// <summary>
/// Writes one empty <see cref="BenchEventRow"/> per <see cref="BenchEvent"/>,
/// partition-keyed by the grain's primary key (the event's RouteKey) and
/// row-keyed by the per-send <c>CorrelationId</c> — so each send produces a
/// distinct row and the issuer's point-get poll waits for the row written by
/// its own send rather than short-circuiting on a stale row from a prior one.
/// </summary>
public sealed partial class BenchProjectionBuilder : EdictTableProjectionBuilder<BenchEventRow>
{
    public const string TableNameLiteral = "benchevent";

    public BenchProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => TableNameLiteral;

    protected override string GetRowKey(EdictEvent edictEvent) => edictEvent switch
    {
        BenchEvent benchEvent => benchEvent.CorrelationId.ToString("D"),
        _ => edictEvent.EventId.ToString("D"),
    };

    public Task HandleAsync(BenchEvent edictEvent) => Task.CompletedTask;
}
