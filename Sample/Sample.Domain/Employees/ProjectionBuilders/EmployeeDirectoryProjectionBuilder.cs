using Edict.Contracts.Events;
using Edict.Contracts.Routing;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using Sample.Contracts.Employees.Events;
using Sample.Contracts.Employees.Projections;

namespace Sample.Domain.Employees.ProjectionBuilders;

/// <summary>
/// The tenant-as-partition employee directory: each <c>EmployeeAddedEvent</c> activates a
/// projection grain keyed <c>"{tenant}|{employeeGuid}"</c>, but the row is stored under the
/// tenant alone, so a company's whole workforce lands in one partition and the ambient-scoped
/// reader lists it back. The partition is recovered from the tenant side of the grain's own
/// composed key, the inverse of the fold that placed it there.
/// </summary>
public sealed partial class EmployeeDirectoryProjectionBuilder : EdictListProjectionBuilder<EmployeeDirectoryRow>
{
    public const string Table = "employeedirectory";

    public EmployeeDirectoryProjectionBuilder(IEdictTableStoreFactory storeFactory)
        : base(storeFactory) { }

    protected override string TableName => Table;

    protected override string DefaultPartitionKey =>
        EdictKeyComposer.Parse(this.GetPrimaryKeyString()).Tenant!.Value.Value;

    protected override string GetRowKey(EdictEvent edictEvent) =>
        edictEvent switch
        {
            EmployeeAddedEvent added => added.EmployeeId.Value.ToString("N"),
            _ => EdictKeyComposer.Parse(this.GetPrimaryKeyString()).RouteKey.ToString("N"),
        };

    Task HandleAsync(EmployeeAddedEvent edictEvent)
    {
        CurrentRow.Name = edictEvent.Name;
        CurrentRow.Department = edictEvent.Department;
        return Task.CompletedTask;
    }
}
