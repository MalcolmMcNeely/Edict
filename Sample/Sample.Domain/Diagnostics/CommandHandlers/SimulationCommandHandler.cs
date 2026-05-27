using Edict.Contracts.Commands;
using Edict.Core.Commands;

using Sample.Contracts.Diagnostics.Commands;
using Sample.Contracts.Diagnostics.Events;
using Sample.Domain.Diagnostics.State;

namespace Sample.Domain.Diagnostics.CommandHandlers;

// Diagnostics-only: this aggregate exists to seed a permanent saga failure path
// for the Dead Letter demo. Production-shaped code does not live in Diagnostics/.
public partial class SimulationCommandHandler : EdictCommandHandler<SimulationState>
{
    public Task<EdictCommandResult> Handle(TriggerSagaFailureCommand command)
    {
        State.Triggered = true;
        Raise(new TriggerSagaFailureEvent(command.SimulationId));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}
