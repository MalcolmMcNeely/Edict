using Edict.Contracts.Commands;
using Edict.Core.Commands;

using Sample.Contracts.Payments.Commands;
using Sample.Contracts.Payments.Domain;
using Sample.Contracts.Payments.Events;
using Sample.Silo.Payments.State;

namespace Sample.Silo.Payments.CommandHandlers;

/// <summary>
/// The second aggregate the OrderPayment saga drives (cross-domain re-keying:
/// the saga forwards the order's Guid as this payment's route key). The
/// authorize/decline decision is a deterministic amount threshold so the sample
/// integration tests can exercise both the happy path and the compensation
/// branch without a test-only seam.
/// </summary>
public partial class PaymentCommandHandler : EdictCommandHandler<PaymentState>
{
    /// <summary>Payments above this amount are declined.</summary>
    public const decimal DeclineThreshold = 1000m;

    public Task<EdictCommandResult> Handle(AuthorizePaymentCommand command)
    {
        State.Amount = command.Amount;

        if (command.Amount > DeclineThreshold)
        {
            State.Status = PaymentStatus.Declined;
            Raise(new PaymentDeclinedEvent(command.OrderId));
        }
        else
        {
            State.Status = PaymentStatus.Authorized;
            Raise(new PaymentAuthorizedEvent(command.OrderId));
        }

        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}
