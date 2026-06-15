using Edict.Contracts.Commands;
using Edict.Telemetry;

namespace Edict.Core.Audit;

// Owns the origin-stamping decision so EdictSender stays a thin Orleans shell.
// Active only when auditing is on; honours a principal already present (the
// explicit escape hatch, and a future relayed re-stamp); fails closed when a
// genuine consumer origin resolves none.
sealed class EdictPrincipalStamper(bool auditEnabled, IEdictPrincipalResolver? resolver)
{
    public TCommand StampAtOrigin<TCommand>(TCommand command) where TCommand : EdictCommand
    {
        if (!auditEnabled || command.Principal is not null)
        {
            return command;
        }

        // A saga's Dispatch and the outbox SendCommand effect re-enter this same
        // send entry point carrying the cross-turn-link marker. They inherit the
        // originating principal through the relay site in a later slice, so they
        // must pass this origin gate untouched rather than fail closed here.
        if (ActivityExtensions.IsCrossTurnLink())
        {
            return command;
        }

        var principal = resolver?.Resolve();
        if (principal is null)
        {
            throw new EdictMissingPrincipalException(command.GetType());
        }

        return command with { Principal = principal };
    }
}
