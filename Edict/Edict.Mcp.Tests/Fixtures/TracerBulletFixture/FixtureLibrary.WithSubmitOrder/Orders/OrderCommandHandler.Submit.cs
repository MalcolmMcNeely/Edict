using Edict.Contracts.Commands;

namespace FixtureLibrary.WithSubmitOrder.Orders;

public sealed partial class OrderCommandHandler
{
    public Task<EdictCommandResult> Handle(SubmitOrderCommand command)
    {
        if (State.IsSubmitted)
        {
            return Task.FromResult<EdictCommandResult>(
                new EdictCommandResult.Rejected(new[]
                {
                    new EdictRejectionReason("already_submitted", "Order has already been submitted."),
                }));
        }

        State.IsSubmitted = true;
        Raise(new OrderSubmittedEvent { OrderId = command.OrderId });
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}
