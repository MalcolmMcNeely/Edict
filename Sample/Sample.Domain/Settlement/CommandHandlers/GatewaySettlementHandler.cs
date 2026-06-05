using Edict.Contracts.Commands;
using Edict.Core.Commands;

using Sample.Contracts.Settlement.Commands;
using Sample.Contracts.Settlement.Events;
using Sample.Domain.Settlement.State;

namespace Sample.Domain.Settlement.CommandHandlers;

/// <summary>
/// Payment aggregate for a gateway settlement, keyed by PaymentId. Begin raises
/// the event the saga schedules off; Confirm and Abandon are the two terminal
/// outcomes the saga dispatches back, one when the poll settles and one when its
/// schedule times out.
/// </summary>
public partial class GatewaySettlementHandler : EdictCommandHandler<GatewaySettlementState>
{
    Task<EdictCommandResult> HandleAsync(BeginGatewaySettlementCommand command)
    {
        State.PaymentId = command.PaymentId;
        Raise(new GatewaySettlementBegunEvent(command.PaymentId, command.PollTimeoutSeconds));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    Task<EdictCommandResult> HandleAsync(ConfirmSettlementCommand command)
    {
        State.Settled = true;
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    Task<EdictCommandResult> HandleAsync(AbandonSettlementCommand command)
    {
        State.Abandoned = true;
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}
