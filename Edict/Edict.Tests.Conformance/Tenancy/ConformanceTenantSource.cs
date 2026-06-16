using Edict.Contracts.Tenancy;

namespace Edict.Tests.Conformance.Tenancy;

/// <summary>
/// The value a tenancy fixture's edge resolver returns, on both the silo and the
/// client (one process, one static). A scenario sets it to "act as" a tenant before a
/// send or a read, so the same fixture drives the headline write, the same-tenant read,
/// and the cross-tenant denial deterministically.
/// </summary>
public static class ConformanceTenantSource
{
    public static EdictTenantId? Current { get; set; }
}
