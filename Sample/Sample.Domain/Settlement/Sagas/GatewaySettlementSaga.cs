using Edict.Contracts.Schedules;
using Edict.Core.Sagas;

using Sample.Contracts.Settlement.Commands;
using Sample.Contracts.Settlement.Events;
using Sample.Contracts.Settlement.Messages;

namespace Sample.Domain.Settlement.Sagas;

/// <summary>
/// Coordinates a gateway settlement by polling on a schedule, the exact same
/// scheduling surface a Command Handler has. On the begin event it schedules a
/// recurring <see cref="PollGatewayMessage"/>; each fire polls and, once the
/// gateway settles, dispatches <see cref="ConfirmSettlementCommand"/> and stops.
/// A schedule started with no timeout is bounded only by the saga's own lifetime,
/// never the command-handler silo default; one started with a timeout runs
/// <see cref="OnScheduleTimeoutAsync"/> on its cap, which dispatches the
/// compensating <see cref="AbandonSettlementCommand"/>.
/// </summary>
public partial class GatewaySettlementSaga : EdictSaga<GatewaySettlementProgress>
{
    // Tuned for demo, not production: a real gateway poll runs on a multi-minute
    // cadence, shrunk here to seconds so a visitor can watch the poll count climb.
    static readonly TimeSpan PollCadence = TimeSpan.FromSeconds(2);

    Task HandleAsync(GatewaySettlementBegunEvent edictEvent)
    {
        Progress.PaymentId = edictEvent.PaymentId;

        if (edictEvent.PollTimeoutSeconds > 0)
        {
            Schedule(new PollGatewayMessage(), every: PollCadence, timeout: TimeSpan.FromSeconds(edictEvent.PollTimeoutSeconds));
        }
        else
        {
            Schedule(new PollGatewayMessage(), every: PollCadence);
        }

        return Task.CompletedTask;
    }

    Task<EdictScheduleResult> HandleAsync(PollGatewayMessage message)
    {
        Progress.Polls++;

        // A real poll would query the gateway; here it settles after the second
        // attempt. Settling sends the confirming Command and stops the schedule.
        if (Progress.Polls >= 2)
        {
            Progress.Outcome = GatewaySettlementOutcome.Settled;
            Dispatch(new ConfirmSettlementCommand(Progress.PaymentId));
            return Task.FromResult<EdictScheduleResult>(new EdictScheduleResult.Complete());
        }

        return Task.FromResult<EdictScheduleResult>(new EdictScheduleResult.Continue());
    }

    Task OnScheduleTimeoutAsync(PollGatewayMessage message)
    {
        Progress.Outcome = GatewaySettlementOutcome.Abandoned;
        Dispatch(new AbandonSettlementCommand(Progress.PaymentId));
        return Task.CompletedTask;
    }
}
