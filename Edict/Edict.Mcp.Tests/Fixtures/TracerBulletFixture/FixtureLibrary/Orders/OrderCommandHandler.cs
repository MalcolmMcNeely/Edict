using Edict.Core.Commands;

namespace FixtureLibrary.Orders;

public sealed partial class OrderCommandHandler : EdictCommandHandler<OrderState>
{
    System.Threading.Tasks.Task HandleAsync(PlaceOrderCommand command) =>
        System.Threading.Tasks.Task.CompletedTask;
}
