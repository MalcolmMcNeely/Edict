using Edict.Contracts.Commands;
using Edict.Core.Commands;

namespace FixtureLibrary.WithSubmitOrder.Orders;

public sealed partial class OrderCommandHandler : EdictCommandHandler<OrderState>
{
    public Task<EdictCommandResult> Handle(PlaceOrderCommand command)
    {
        Raise(new OrderPlaced { OrderId = command.OrderId });
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}
