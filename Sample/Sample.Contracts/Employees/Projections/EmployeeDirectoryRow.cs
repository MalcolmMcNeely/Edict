using Edict.Contracts.Persistence;

namespace Sample.Contracts.Employees.Projections;

/// <summary>
/// One row of a company's employee directory: a List-projection row partitioned by
/// the tenant, so a business reads its own whole workforce from one partition while
/// another tenant's identical query is empty by construction. The row key is the
/// employee, the partition is the tenant recovered from the projection grain's own
/// composed key.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Contracts.Employees.Projections.EmployeeDirectoryRow")]
public sealed class EmployeeDirectoryRow : IEdictPersistedState
{
    [Id(0)]
    public string Name { get; set; } = string.Empty;

    [Id(1)]
    public string Department { get; set; } = string.Empty;
}
