using Edict.Contracts.Commands;

using MessagePack;

namespace Sample.Silo.Orders;

[MessagePackObject(keyAsPropertyName: true)]
public sealed partial record SubmitOrderCommand(Guid OrderId) : Command
{
    [RouteKey]
    public Guid OrderId { get; init; } = OrderId;
}
