using Edict.Contracts.Persistence;

using Sample.Contracts.Orders.Domain;

namespace Sample.Silo.Orders.State;

/// <summary>
/// Framework-owned durable aggregate state for an order. The
/// consumer mutates this inside its <c>Handle</c> methods; Edict commits it
/// atomically with the Outbox in one grain-document write. Persisted grain
/// state, so a frozen string-literal <c>[Alias]</c> survives a class rename
///; <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Silo.Orders.OrderState")]
public sealed class OrderState : IEdictPersistedState
{
    [Id(0)]
    public OrderStatus Status { get; set; } = OrderStatus.Open;

    [Id(1)]
    public List<OrderLine> Items { get; set; } = [];
}
