using Edict.Contracts.Commands;
using Edict.Contracts.Events;

using Sample.Contracts.Employees.Domain;

namespace Sample.Contracts.Employees.Events;

[EdictStream("Employees")]
public sealed partial record EmployeeAddedEvent(
    [property: EdictRouteKey] EmployeeId EmployeeId,
    string Name,
    string Department) : EdictEvent;
