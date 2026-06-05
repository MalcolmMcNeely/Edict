using Edict.Contracts.Commands;
using Edict.Contracts.Schedules;
using Edict.Core.Commands;

using Sample.Contracts.Delivery.Commands;
using Sample.Contracts.Delivery.Events;
using Sample.Contracts.Delivery.Messages;
using Sample.Domain.Delivery.State;

namespace Sample.Domain.Delivery.CommandHandlers;

/// <summary>
/// Delivery aggregate, keyed by OrderId. When an order ships it schedules a
/// <see cref="DeliveryEtaTick"/> on a daily cadence; each fire ticks the ETA down
/// a day, raises <see cref="DeliveryEtaTickedEvent"/>, and on arrival raises
/// <see cref="DeliveredEvent"/> and completes the schedule. The whole loop is
/// durable and survives deactivation: the cadence is declared once at
/// <c>Schedule(...)</c>, and the fire handler answers only "again or done".
/// </summary>
public partial class DeliveryTrackerCommandHandler : EdictCommandHandler<DeliveryTrackerState>
{
    Task<EdictCommandResult> HandleAsync(StartDeliveryTrackingCommand command)
    {
        if (State.EtaDaysRemaining > 0)
        {
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("delivery_already_tracking", "This order is already being tracked for delivery.")]));
        }

        State.OrderId = command.OrderId;
        State.EtaDaysRemaining = command.EtaDays;
        Schedule(new DeliveryEtaTick(), every: TimeSpan.FromDays(1));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    Task<EdictScheduleResult> HandleAsync(DeliveryEtaTick message)
    {
        State.EtaDaysRemaining--;
        Raise(new DeliveryEtaTickedEvent(State.OrderId, State.EtaDaysRemaining));

        if (State.EtaDaysRemaining <= 0)
        {
            Raise(new DeliveredEvent(State.OrderId));
            return Complete();
        }

        return Continue();
    }
}
