using Edict.Contracts.Commands;
using Edict.Telemetry;

namespace Edict.Core.Audit;

// Owns the origin-stamping decision so EdictSender stays a thin Orleans shell.
// Active only when auditing is on; honours a principal already present (the
// explicit escape hatch, and a relayed command the per-turn relay already
// stamped); fails closed when a genuine consumer origin resolves none.
sealed class EdictPrincipalStamper(bool auditEnabled, IEdictPrincipalResolver? resolver)
{
    public TCommand StampAtOrigin<TCommand>(TCommand command) where TCommand : EdictCommand
    {
        if (!auditEnabled || command.Principal is not null)
        {
            return command;
        }

        // A saga's Dispatch and the outbox SendCommand effect re-enter this same
        // send entry point carrying the cross-turn-link marker. The per-turn relay
        // stamps the inherited actor onto them, so an audited chain short-circuits
        // above on the present field; this gate stays as the exemption for a
        // relayed send that legitimately carries no principal (a raw timer-fire
        // event, or a pre-audit entry draining later), which must pass untouched
        // rather than fail closed on the relayed path.
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
