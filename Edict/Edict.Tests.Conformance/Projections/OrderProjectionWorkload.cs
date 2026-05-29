using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Idempotency;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Tests.Conformance.Projections;

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Projections.OrderTableRow")]
public sealed class OrderTableRow : IEdictPersistedState
{
    [Id(0)]
    public int OrderCount { get; set; }
}

public interface IOrderTableProjectionProbe : IGrainWithGuidKey
{
    Task<RingStateProbe> GetRingStateAsync();
}

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Projections.RingStateProbe")]
public sealed record RingStateProbe(
    [property: Id(0)] int Capacity,
    [property: Id(1)] int Count);

public sealed partial class OrderTableProjectionBuilder
    : EdictTableProjectionBuilder<OrderTableRow>, IOrderTableProjectionProbe
{
    public OrderTableProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "orderprojection";

    protected override string GetRowKey(EdictEvent edictEvent) =>
        edictEvent switch
        {
            OrderPlacedEvent placed => placed.OrderId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    public Task Handle(OrderPlacedEvent edictEvent)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }

    public Task<RingStateProbe> GetRingStateAsync() =>
        Task.FromResult(new RingStateProbe(
            State.Idempotency.HandledEventIds.Length,
            State.Idempotency.Count));
}

// Consumer-specified fixed RowKey ("summary") — proves RowKey is independent
// of PartitionKey.
public sealed partial class OrderSummaryTableProjectionBuilder : EdictTableProjectionBuilder<OrderTableRow>
{
    public OrderSummaryTableProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "ordersummary";

    protected override string GetRowKey(EdictEvent edictEvent) => "summary";

    public Task Handle(OrderPlacedEvent edictEvent)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }
}

// Global-singleton projection grain at a fixed Guid key. RowKey is the
// source aggregate ID, so each aggregate's order is a distinct row under
// the singleton PartitionKey.
public sealed partial class GlobalOrderTableProjectionBuilder : EdictTableProjectionBuilder<OrderTableRow>
{
    public static readonly Guid SingletonKey = new("00000000-0000-0000-0000-000000000001");

    public GlobalOrderTableProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "globalorderprojection";

    protected override string GetRowKey(EdictEvent edictEvent) =>
        edictEvent switch
        {
            OrderPlacedEvent placed => placed.OrderId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    public Task Handle(OrderPlacedEvent edictEvent)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }
}

public interface IOrderProjectionAccess : IGrainWithGuidKey
{
    Task<int> GetOrderCountAsync();
}

public sealed partial class OrderProjectionBuilder : EdictProjectionBuilder, IOrderProjectionAccess
{
    int _orderCount;

    public Task<int> GetOrderCountAsync() => Task.FromResult(_orderCount);

    public Task Handle(OrderPlacedEvent edictEvent)
    {
        _orderCount++;
        return Task.CompletedTask;
    }
}

[EdictStream("ConformanceOrders")]
public sealed partial record UnknownOrderEvent(Guid AggregateId) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;
}

public interface IStreamPublisher : IGrainWithGuidKey
{
    Task PublishAsync(string streamName, EdictEvent edictEvent);
}

public sealed class StreamPublisher : Grain, IStreamPublisher
{
    public Task PublishAsync(string streamName, EdictEvent edictEvent) =>
        this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create(streamName, this.GetPrimaryKey()))
            .OnNextAsync(edictEvent);
}
