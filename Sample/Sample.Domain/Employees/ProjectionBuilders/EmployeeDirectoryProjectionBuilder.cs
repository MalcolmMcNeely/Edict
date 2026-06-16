using Edict.Core.Projections;

using Sample.Contracts.Employees.Events;
using Sample.Contracts.Employees.Projections;

namespace Sample.Domain.Employees.ProjectionBuilders;

public sealed partial class EmployeeDirectoryProjectionBuilder : EdictProjectionBuilder<EmployeeDirectoryRow>
{
    Task HandleAsync(EmployeeAddedEvent edictEvent)
    {
        Projection.Department = edictEvent.Department;
        return Task.CompletedTask;
    }
}
