using Edict.Contracts.Persistence;

namespace Sample.Domain.Employees.State;

/// <summary>
/// Framework-owned durable aggregate state for an employee. Persisted grain state,
/// so a frozen string-literal <c>[Alias]</c> survives a class rename;
/// <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Silo.Employees.EmployeeState")]
public sealed class EmployeeState : IEdictPersistedState
{
    [Id(0)]
    public string Name { get; set; } = string.Empty;

    [Id(1)]
    public string Department { get; set; } = string.Empty;
}
