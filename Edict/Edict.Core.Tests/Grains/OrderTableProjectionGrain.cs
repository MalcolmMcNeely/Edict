using Edict.Contracts.Events;
using Edict.Core.Grains;
using Edict.Core.TableStorage;

namespace Edict.Core.Tests.Grains;

/// <summary>Table row for the order table projection test — plain POCO, no storage keys.</summary>
public sealed class OrderTableRow
{
    public int OrderCount { get; set; }
}

/// <summary>
/// Test-only table projection grain. Counts orders per aggregate.
/// RowKey = OrderId (same as PartitionKey for per-aggregate projections).
/// </summary>
public sealed partial class OrderTableProjectionGrain : EdictTableProjectionBuilderGrain<OrderTableRow>
{
    public OrderTableProjectionGrain(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "orderprojection";

    protected override string GetRowKey(EdictEvent evt) =>
        evt switch
        {
            OrderPlacedEvent placed => placed.OrderId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    public Task Handle(OrderPlacedEvent evt)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Test-only grain with a consumer-specified fixed RowKey ("summary"), showing that
/// the RowKey is independent of the PartitionKey.
/// </summary>
public sealed partial class OrderSummaryTableProjectionGrain : EdictTableProjectionBuilderGrain<OrderTableRow>
{
    public OrderSummaryTableProjectionGrain(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "ordersummary";

    protected override string GetRowKey(EdictEvent evt) => "summary";

    public Task Handle(OrderPlacedEvent evt)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Test-only global-singleton projection grain. Activated at a fixed Guid key and
/// receives events published directly to its stream key. RowKey = source aggregate ID,
/// so each aggregate's order is a distinct row under the singleton PartitionKey.
/// </summary>
public sealed partial class GlobalOrderTableProjectionGrain : EdictTableProjectionBuilderGrain<OrderTableRow>
{
    public static readonly Guid SingletonKey = new("00000000-0000-0000-0000-000000000001");

    public GlobalOrderTableProjectionGrain(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "globalorderprojection";

    protected override string GetRowKey(EdictEvent evt) =>
        evt switch
        {
            OrderPlacedEvent placed => placed.OrderId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    public Task Handle(OrderPlacedEvent evt)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }
}
