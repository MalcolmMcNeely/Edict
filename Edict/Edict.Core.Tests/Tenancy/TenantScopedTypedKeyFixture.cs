using Edict.Contracts.Commands;
using Edict.Contracts.Tenancy;

using MessagePack;

namespace Edict.Core.Tests.Tenancy;

// A typed, tenant-scoped route key: the marker sits on the route-key type, and the
// command carries it as its [EdictRouteKey]. The MessagePack attribute mirrors
// EdictTenantId so the value type rides the contract wire the same way.
[EdictTenantScoped]
[MessagePackObject(keyAsPropertyName: true)]
public readonly record struct EmployeeId(Guid Value);

public sealed partial record AddEmployeeCommand(EmployeeId EmployeeId, string Department) : EdictCommand
{
    [EdictRouteKey]
    public EmployeeId EmployeeId { get; init; } = EmployeeId;

    public string Department { get; init; } = Department;
}
