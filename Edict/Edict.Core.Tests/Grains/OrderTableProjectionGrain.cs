using Azure;
using Azure.Data.Tables;

using Edict.Contracts.Events;
using Edict.Core.Grains;

namespace Edict.Core.Tests.Grains;

/// <summary>Table entity for the order table projection test.</summary>
public sealed class OrderTableRow : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public int OrderCount { get; set; }
}

/// <summary>
/// Test-only table projection grain. Counts orders per aggregate via Azure Table Storage.
/// RowKey = OrderId (same as PartitionKey for per-aggregate projections).
/// </summary>
public sealed partial class OrderTableProjectionGrain : TableProjectionBuilderGrain<OrderTableRow>
{
    public OrderTableProjectionGrain(Azure.Data.Tables.TableServiceClient tableServiceClient)
        : base(tableServiceClient) { }

    protected override string TableName => "orderprojection";

    protected override string GetRowKey(Event evt) =>
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
public sealed partial class OrderSummaryTableProjectionGrain : TableProjectionBuilderGrain<OrderTableRow>
{
    public OrderSummaryTableProjectionGrain(Azure.Data.Tables.TableServiceClient tableServiceClient)
        : base(tableServiceClient) { }

    protected override string TableName => "ordersummary";

    protected override string GetRowKey(Event evt) => "summary";

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
public sealed partial class GlobalOrderTableProjectionGrain : TableProjectionBuilderGrain<OrderTableRow>
{
    public static readonly Guid SingletonKey = new("00000000-0000-0000-0000-000000000001");

    public GlobalOrderTableProjectionGrain(Azure.Data.Tables.TableServiceClient tableServiceClient)
        : base(tableServiceClient) { }

    protected override string TableName => "globalorderprojection";

    protected override string GetRowKey(Event evt) =>
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
