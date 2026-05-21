using Edict.Contracts.Commands;
using Edict.Core.Commands;

using Sample.Contracts.Diagnostics.Commands;
using Sample.Silo.Diagnostics.State;

namespace Sample.Silo.Diagnostics.CommandHandlers;

// Diagnostics-only: every command returns Rejected so the saga's SendCommand
// outbox effect exhausts OutboxMaxAttempts and the engine promotes to Dead
// Letter. Production-shaped code does not live in Diagnostics/.
public partial class RejectingCommandHandler : EdictCommandHandler<RejectingState>
{
    public Task<EdictCommandResult> Handle(RejectingCommand command)
    {
        State.RejectedCount++;
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Rejected(
            [new EdictRejectionReason("rejecting_command_handler", "RejectingCommandHandler rejects every command by design.")]));
    }
}
