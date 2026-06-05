using Edict.Contracts.Commands;
using Edict.Contracts.Schedules;
using Edict.Core.Commands;

using Microsoft.Extensions.DependencyInjection;

using Sample.Contracts.Fulfillment.Commands;
using Sample.Contracts.Fulfillment.Domain;
using Sample.Contracts.Fulfillment.Events;
using Sample.Contracts.Fulfillment.Messages;
using Sample.Domain.Fulfillment.State;

namespace Sample.Domain.Fulfillment.CommandHandlers;

/// <summary>
/// Fulfillment aggregate, keyed by OrderId. Snapshots the line item ids from
/// <see cref="StartFulfillmentCommand"/> and schedules a <see cref="FulfillNextLine"/>
/// on a flat cadence; each fire transitions one Pending line to Fulfilled and
/// raises <see cref="LineItemFulfilledEvent"/>. The terminal fire raises
/// <see cref="OrderFullyFulfilledEvent"/> and completes the schedule. The whole
/// loop is durable and survives deactivation: the cadence is declared once at
/// <c>Schedule(...)</c>, and the fire handler answers only "again or done".
/// </summary>
public partial class FulfillmentCommandHandler : EdictCommandHandler<FulfillmentState>
{
    Task<EdictCommandResult> HandleAsync(StartFulfillmentCommand command)
    {
        if (State.Lines.Count > 0)
        {
            return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
                [new EdictRejectionReason("fulfillment_already_started", "Fulfillment has already been started for this order.")]));
        }

        State.OrderId = command.OrderId;
        State.Lines = command.LineItemIds
            .Select(id => new FulfillmentLine { LineItemId = id, Status = LineItemFulfillmentStatus.Pending })
            .ToList();

        Schedule(new FulfillNextLine(), every: TimeSpan.FromSeconds(2));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    async Task<EdictScheduleResult> HandleAsync(FulfillNextLine message)
    {
        var pendingIndex = State.Lines.FindIndex(candidate => candidate.Status == LineItemFulfillmentStatus.Pending);
        if (pendingIndex < 0)
        {
            return await Complete();
        }

        var line = State.Lines[pendingIndex];
        if (ServiceProvider.GetService<IWarehouseGateway>() is { } gateway)
        {
            await gateway.DispatchLineAsync(State.OrderId, line.LineItemId);
        }

        State.Lines[pendingIndex] = line with { Status = LineItemFulfillmentStatus.Fulfilled };
        Raise(new LineItemFulfilledEvent(State.OrderId, line.LineItemId));

        if (State.Lines.All(candidate => candidate.Status == LineItemFulfillmentStatus.Fulfilled))
        {
            Raise(new OrderFullyFulfilledEvent(State.OrderId));
            return await Complete();
        }

        return await Continue();
    }
}
