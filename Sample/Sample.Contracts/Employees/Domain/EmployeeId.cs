using Edict.Contracts.Tenancy;

using MessagePack;

namespace Sample.Contracts.Employees.Domain;

/// <summary>
/// The tenant-scoped route key for the Employee aggregate. An employee's data
/// belongs to their employing tenant, so the marker walls every Employee message
/// per tenant, sitting beside the public Order and Cart aggregates in one shared
/// deployment. Typed rather than a bare Guid so an OrderId can never be passed
/// where an EmployeeId is meant; the MessagePack attribute mirrors EdictTenantId
/// so the value type rides the contract wire the same way.
/// </summary>
[EdictTenantScoped]
[MessagePackObject(keyAsPropertyName: true)]
public readonly record struct EmployeeId(Guid Value);
