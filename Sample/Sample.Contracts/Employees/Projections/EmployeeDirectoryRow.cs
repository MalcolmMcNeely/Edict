using Edict.Contracts.Persistence;

namespace Sample.Contracts.Employees.Projections;

[GenerateSerializer]
[Alias("Sample.Contracts.Employees.Projections.EmployeeDirectoryRow")]
public sealed class EmployeeDirectoryRow : IEdictPersistedState
{
    [Id(0)]
    public string Department { get; set; } = string.Empty;
}
