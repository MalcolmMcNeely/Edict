using Edict.Contracts.Commands;
using Edict.Core.Commands;

using Orleans.Runtime;

using Sample.Contracts.Fulfillment.Commands;
using Sample.Contracts.Fulfillment.Domain;
using Sample.Contracts.Fulfillment.Events;
using Sample.Domain.Fulfillment.State;

namespace Sample.Domain.Fulfillment.CommandHandlers;

/// <summary>
/// Fulfillment aggregate, keyed by OrderId. Snapshots the line item ids from
/// <see cref="StartFulfillmentCommand"/> and registers an Orleans grain timer
/// that transitions one Pending line to Fulfilled per tick at a randomised
/// 2–8s cadence; the terminal tick raises <see cref="OrderFullyFulfilledEvent"/>
/// and stops the timer.
/// <para>
/// The grain timer is the deliberate demo choice over a Reminder because Orleans
/// Reminders have a one-minute minimum period — too coarse for the sample's
/// sub-10s tick cadence. Production code with longer cadences should use
/// reminders for durability across deactivation; the demo accepts the trade
/// because the timer is reseeded inside the same activation and the in-process
/// cluster never deactivates while the workflow is in flight.
/// </para>
/// </summary>
public partial class FulfillmentCommandHandler : EdictCommandHandler<FulfillmentState>
{
    IGrainTimer? _timer;

    public Task<EdictCommandResult> HandleAsync(StartFulfillmentCommand command)
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

        ScheduleNextTick();
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    void ScheduleNextTick()
    {
        var delay = TimeSpan.FromSeconds(Random.Shared.Next(2, 8));
        _timer?.Dispose();
        _timer = this.RegisterGrainTimer(OnTickAsync, new GrainTimerCreationOptions
        {
            DueTime = delay,
            Period = Timeout.InfiniteTimeSpan,
            KeepAlive = true,
        });
    }

    async Task OnTickAsync(CancellationToken cancellationToken)
    {
        var pendingIndex = State.Lines.FindIndex(l => l.Status == LineItemFulfillmentStatus.Pending);
        if (pendingIndex < 0)
        {
            _timer?.Dispose();
            _timer = null;
            return;
        }

        var line = State.Lines[pendingIndex];
        State.Lines[pendingIndex] = line with { Status = LineItemFulfillmentStatus.Fulfilled };
        Raise(new LineItemFulfilledEvent(State.OrderId, line.LineItemId));

        var allFulfilled = State.Lines.All(l => l.Status == LineItemFulfillmentStatus.Fulfilled);
        if (allFulfilled)
        {
            Raise(new OrderFullyFulfilledEvent(State.OrderId));
            _timer?.Dispose();
            _timer = null;
        }
        else
        {
            ScheduleNextTick();
        }

        await CommitAndDrainRaisedEventsAsync();
    }
}
