using Edict.Contracts;
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

public interface IOrderListProjectionProbe : IGrainWithGuidKey
{
    Task<RingStateProbe> GetRingStateAsync();

    Task DeactivateSelfAsync();
}

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Projections.RingStateProbe")]
public sealed record RingStateProbe(
    [property: Id(0)] int Capacity,
    [property: Id(1)] int Count);

public sealed partial class OrderListProjectionBuilder
    : EdictListProjectionBuilder<OrderTableRow>, IOrderListProjectionProbe
{
    public OrderListProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "orderprojection";

    protected override string GetRowKey(EdictEvent edictEvent) =>
        edictEvent switch
        {
            OrderPlacedEvent placed => placed.OrderId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    Task HandleAsync(OrderPlacedEvent edictEvent)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }

    public Task<RingStateProbe> GetRingStateAsync() =>
        Task.FromResult(new RingStateProbe(
            State.Idempotency.HandledEventIds.Length,
            State.Idempotency.Count));

    public Task DeactivateSelfAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }
}

// Consumer-specified fixed RowKey ("summary") — proves RowKey is independent
// of PartitionKey.
public sealed partial class OrderSummaryProjectionBuilder : EdictListProjectionBuilder<OrderTableRow>
{
    public OrderSummaryProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "ordersummary";

    protected override string GetRowKey(EdictEvent edictEvent) => "summary";

    Task HandleAsync(OrderPlacedEvent edictEvent)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }
}

// Global-singleton projection grain at a fixed Guid key. RowKey is the
// source aggregate ID, so each aggregate's order is a distinct row under
// the singleton PartitionKey.
public sealed partial class GlobalOrderProjectionBuilder : EdictListProjectionBuilder<OrderTableRow>
{
    public static readonly Guid SingletonKey = new("00000000-0000-0000-0000-000000000001");

    public GlobalOrderProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => "globalorderprojection";

    protected override string GetRowKey(EdictEvent edictEvent) =>
        edictEvent switch
        {
            OrderPlacedEvent placed => placed.OrderId.ToString(),
            _ => this.GetPrimaryKey().ToString(),
        };

    Task HandleAsync(OrderPlacedEvent edictEvent)
    {
        CurrentRow.OrderCount++;
        return Task.CompletedTask;
    }
}

public interface IOrderProjectionAccess : IGrainWithGuidKey
{
    Task<int> GetOrderCountAsync();

    /// <summary>
    /// The high-water mark of handler turns that were in-flight on this one
    /// activation at the same moment. One means every event was delivered and
    /// handled to completion before the next began; greater than one means the
    /// runtime ran two turns of this activation concurrently. Used by the
    /// real-stream delivery-serialization guard.
    /// </summary>
    Task<int> GetMaxConcurrentHandlerTurnsAsync();
}

/// <summary>
/// Sentinel an <see cref="OrderProjectionWorkload"/>-style burst test stamps on
/// <c>PlaceOrderCommand.Sku</c> so <see cref="OrderProjectionBuilder"/> only arms
/// its turn-overlap instrumentation for that test's events, leaving every other
/// scenario's handler timing untouched.
/// </summary>
public static class ConcurrencyProbe
{
    public const string Sku = "edict-delivery-serialization-probe";

    // Long enough that an entire burst of events for one aggregate arrives while
    // the first turn is still parked here: if the runtime ever ran a second turn
    // of the activation concurrently, the high-water mark would spike well above
    // one. Serial delivery pins it at one regardless.
    public static readonly TimeSpan HandlerHold = TimeSpan.FromMilliseconds(750);
}

public sealed partial class OrderProjectionBuilder : EdictProjectionBuilderBase<EdictUnit>, IOrderProjectionAccess
{
    int _orderCount;
    int _activeHandlerTurns;
    int _maxConcurrentHandlerTurns;

    public Task<int> GetOrderCountAsync() => Task.FromResult(_orderCount);

    public Task<int> GetMaxConcurrentHandlerTurnsAsync() => Task.FromResult(_maxConcurrentHandlerTurns);

    async Task HandleAsync(OrderPlacedEvent edictEvent)
    {
        if (edictEvent.Sku != ConcurrencyProbe.Sku)
        {
            _orderCount++;
            return;
        }

        // Hold the turn parked at an await. If the runtime ever delivered a
        // second event to this activation before this turn finished, both turns
        // would be counted in-flight here and the high-water mark would exceed
        // one. A real Orleans pulling agent delivers one stream's events to a
        // single consumer serially, so it stays at one.
        var active = Interlocked.Increment(ref _activeHandlerTurns);
        if (active > _maxConcurrentHandlerTurns)
        {
            _maxConcurrentHandlerTurns = active;
        }
        try
        {
            await Task.Delay(ConcurrencyProbe.HandlerHold);
        }
        finally
        {
            Interlocked.Decrement(ref _activeHandlerTurns);
        }
        _orderCount++;
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
