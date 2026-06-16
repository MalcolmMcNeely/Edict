using Edict.Contracts.Tenancy;

namespace Edict.Core.Tenancy;

/// <summary>
/// Thrown by the isolation call filter when a command call lands on a grain whose key
/// names a tenant other than the calling turn's ambient tenant, without the crossing
/// being explicitly authorized. On the common path this never fires: every key is
/// composed from the ambient tenant, so a grain's key-tenant equals the relay tenant by
/// construction. It surfaces only on a real divergence — a coding bug that formed a key
/// outside the composition chokepoint, or an illegitimate attempt to reach into another
/// wall — so it fails the call loud rather than letting a cross-tenant access through.
/// </summary>
public sealed class EdictCrossTenantAccessException : Exception
{
    public EdictCrossTenantAccessException(EdictTenantId? relayTenant, EdictTenantId? keyTenant)
        : base($"A call from tenant '{Describe(relayTenant)}' was routed to a grain in tenant '{Describe(keyTenant)}'. "
            + "Cross-tenant access is denied unless explicitly authorized through the establishing-crossing send overload "
            + "or the operator path.")
    {
    }

    static string Describe(EdictTenantId? tenant) => tenant?.Value ?? "(none)";
}
