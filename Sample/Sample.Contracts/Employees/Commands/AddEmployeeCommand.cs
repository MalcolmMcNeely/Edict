using Edict.Contracts.Commands;

using Sample.Contracts.Employees.Domain;

namespace Sample.Contracts.Employees.Commands;

public sealed partial record AddEmployeeCommand(
    [property: EdictRouteKey] EmployeeId EmployeeId,
    string Name,
    string Department) : EdictCommand;
