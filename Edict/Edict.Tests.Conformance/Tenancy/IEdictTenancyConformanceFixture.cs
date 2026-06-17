using Edict.Contracts.Projections;

namespace Edict.Tests.Conformance.Tenancy;

/// <summary>
/// The tenancy capability a persistence-axis fixture adds when it wires
/// <c>AddEdictTenant</c> on its silo and client. It exposes the ambient-scoped
/// reader the tenant isolation scenarios read through; the rest of the surface
/// (the send seam, the durable table store) comes from
/// <c>PersistenceConformanceFixture</c>. A scenario sets the ambient tenant via
/// <see cref="ConformanceTenantSource"/> before each send or read, so the same
/// fixture drives the headline write and the cross-tenant read that comes back
/// empty by construction.
/// </summary>
public interface IEdictTenancyConformanceFixture
{
    IEdictTenantScopedListProjectionReader<EmployeeDirectoryRow> EmployeeDirectoryReader { get; }
}
