namespace FixtureLibrary.Orders;

public sealed partial class OrderCommandHandler
{
    public System.Threading.Tasks.Task Handle(CancelOrderCommand command) =>
        System.Threading.Tasks.Task.CompletedTask;
}
