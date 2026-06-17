using Edict.Contracts.Tenancy;

using Orleans;

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
/// <remarks>
/// The filter runs inside the silo and a stolen-key reach is a direct grain call from
/// the client, so this exception is serialized back across that hop. It carries an
/// Orleans codec so the denial reaches the caller as itself, with its message intact,
/// rather than as an opaque serialization failure that hides why the call was refused.
/// </remarks>
[GenerateSerializer]
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
