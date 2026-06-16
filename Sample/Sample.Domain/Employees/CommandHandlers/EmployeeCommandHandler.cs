using Edict.Contracts.Commands;
using Edict.Core.Commands;

using Sample.Contracts.Employees.Commands;
using Sample.Contracts.Employees.Events;
using Sample.Domain.Employees.State;

namespace Sample.Domain.Employees.CommandHandlers;

/// <summary>
/// Employee aggregate, keyed by the tenant-scoped <c>EmployeeId</c>. It demonstrates
/// a tenant-scoped aggregate living beside the public Order and Cart aggregates in
/// one shared deployment: its grain and stream keys route through the same
/// composition chokepoint as a public aggregate, differing only in what the
/// route-key type's marker folds into the key.
/// </summary>
public partial class EmployeeCommandHandler : EdictCommandHandler<EmployeeState>
{
    Task<EdictCommandResult> HandleAsync(AddEmployeeCommand command)
    {
        State.Department = command.Department;
        Raise(new EmployeeAddedEvent(command.EmployeeId, command.Department));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}
