using Edict.Contracts.Commands;
using Edict.Telemetry;

namespace Edict.Core.Tenancy;

// Owns the origin-tenant-stamping decision so EdictSender stays a thin Orleans
// shell, the sibling of EdictPrincipalStamper. Active only when tenancy is on;
// honours a tenant already present (the explicit establishing-crossing overload,
// and a relayed command the per-turn relay already stamped); fails closed when a
// genuine consumer origin resolves none.
sealed class EdictTenantStamper(bool tenantEnabled, IEdictTenantResolver? resolver)
{
    public TCommand StampAtOrigin<TCommand>(TCommand command) where TCommand : EdictCommand
    {
        if (!tenantEnabled || command.Tenant is not null)
        {
            return command;
        }

        // A saga's Dispatch and the outbox SendCommand effect re-enter this same
        // send entry point carrying the cross-turn-link marker. The per-turn relay
        // stamps the inherited tenant onto them, so a tenant-scoped chain
        // short-circuits above on the present field; this gate stays as the
        // exemption for a relayed send that legitimately carries no tenant (a raw
        // timer-fire event, a public-aggregate chain), which must pass untouched
        // rather than fail closed on the relayed path.
        if (ActivityExtensions.IsCrossTurnLink())
        {
            return command;
        }

        var tenant = resolver?.Resolve();
        if (tenant is null)
        {
            throw new EdictMissingTenantException(command.GetType());
        }

        return command with { Tenant = tenant };
    }
}
