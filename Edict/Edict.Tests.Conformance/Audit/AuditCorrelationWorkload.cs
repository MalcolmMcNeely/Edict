using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Core.Commands;
using Edict.Core.Sagas;

using Orleans;

namespace Edict.Tests.Conformance.Audit;

// A two-aggregate workflow joined by a saga, so an audit query scenario has a
// correlation that genuinely spans more than one grain. Placing an order on the
// OrderAggregate raises OrderPlacedEvent; the FulfilmentSaga reacts and dispatches
// ReserveStockCommand to the StockAggregate, which raises StockReservedEvent. The
// origin correlation and principal propagate across the saga hop, so the four
// captured records (C1+E1 on each aggregate) share one correlation and one
// principal across two distinct entity types.

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Audit.OrderState")]
public sealed class OrderState : IEdictPersistedState
{
    [Id(0)]
    public int Placed { get; set; }
}

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Audit.StockState")]
public sealed class StockState : IEdictPersistedState
{
    [Id(0)]
    public int Reserved { get; set; }
}

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Audit.FulfilmentProgress")]
public sealed class FulfilmentProgress : IEdictPersistedState
{
    [Id(0)]
    public int Handled { get; set; }
}

public sealed partial record PlaceOrderCommand(Guid OrderId) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

public sealed partial record ReserveStockCommand(Guid OrderId) : EdictCommand
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

[EdictStream("AuditCorrelationOrders")]
public sealed partial record OrderPlacedEvent(Guid OrderId) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

[EdictStream("AuditCorrelationStock")]
public sealed partial record StockReservedEvent(Guid OrderId) : EdictEvent
{
    [EdictRouteKey]
    public Guid OrderId { get; init; } = OrderId;
}

public partial class OrderAggregate : EdictCommandHandler<OrderState>
{
    Task<EdictCommandResult> HandleAsync(PlaceOrderCommand command)
    {
        State.Placed++;
        Raise(new OrderPlacedEvent(command.OrderId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

public partial class FulfilmentSaga : EdictSaga<FulfilmentProgress>
{
    Task HandleAsync(OrderPlacedEvent edictEvent)
    {
        Progress.Handled++;
        Dispatch(new ReserveStockCommand(edictEvent.OrderId));
        return Task.CompletedTask;
    }
}

public partial class StockAggregate : EdictCommandHandler<StockState>
{
    Task<EdictCommandResult> HandleAsync(ReserveStockCommand command)
    {
        State.Reserved++;
        Raise(new StockReservedEvent(command.OrderId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}
