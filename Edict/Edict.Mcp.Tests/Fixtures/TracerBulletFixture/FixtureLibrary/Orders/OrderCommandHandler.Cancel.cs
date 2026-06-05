namespace FixtureLibrary.Orders;

public sealed partial class OrderCommandHandler
{
    System.Threading.Tasks.Task HandleAsync(CancelOrderCommand command) =>
        System.Threading.Tasks.Task.CompletedTask;
}
