using Edict.Core.Sagas;

using Sample.Contracts.Diagnostics.Commands;
using Sample.Contracts.Diagnostics.Events;

namespace Sample.Domain.Diagnostics.Sagas;

// Diagnostics-only: dispatches a command that the target handler always rejects.
// The SendCommand outbox effect retries until OutboxMaxAttempts exhausts, then
// the engine promotes the entry to Dead Letter. Production-shaped code does not
// live in Diagnostics/.
public partial class BadCommandSaga : EdictSaga<BadCommandSagaProgress>
{
    public Task HandleAsync(TriggerSagaFailureEvent edictEvent)
    {
        Progress.Stage = BadCommandSagaStage.Dispatched;
        Dispatch(new RejectingCommand(edictEvent.SimulationId));
        return Task.CompletedTask;
    }
}
