using Edict.Contracts.Commands;

namespace FixtureLibrary.WithSubmitOrder.Orders;

public sealed partial class OrderCommandHandler
{
    public Task<EdictCommandResult> Handle(CancelOrderCommand command)
    {
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}
