using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;
using Edict.Core.Commands;
using Edict.Core.Projections;
using Edict.Core.TableStorage;

using MessagePack;

using Orleans;
using Orleans.Runtime;

namespace Edict.Tests.Conformance.Tenancy;

// The headline aggregate: a tenant-scoped Employee written under one tenant and read
// back per tenant. The route-key type carries [EdictTenantScoped], so every command,
// event, and projection-grain key for an Employee folds the tenant through the one
// composition chokepoint. The List projection partitions by tenant, so "list my
// employees" reads one tenant's partition and another tenant's identical query is
// empty by construction, not by a permission check.
[EdictTenantScoped]
[MessagePackObject(keyAsPropertyName: true)]
public readonly record struct EmployeeId(Guid Value);

public sealed partial record AddEmployeeCommand(
    [property: EdictRouteKey] EmployeeId EmployeeId,
    string Department) : EdictCommand;

[EdictStream("ConformanceEmployees")]
public sealed partial record EmployeeAddedEvent(
    [property: EdictRouteKey] EmployeeId EmployeeId,
    string Department) : EdictEvent;

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Tenancy.EmployeeState")]
public sealed class EmployeeState : IEdictPersistedState
{
    [Id(0)]
    public string Department { get; set; } = string.Empty;
}

[GenerateSerializer]
[Alias("Edict.Tests.Conformance.Tenancy.EmployeeDirectoryRow")]
public sealed class EmployeeDirectoryRow : IEdictPersistedState
{
    [Id(0)]
    public string Department { get; set; } = string.Empty;
}

// Hand-written probe (Orleans codegen can't see the Edict-generated grain
// interface) so a dead-letter scenario can drive the outbox drain reminder
// deterministically. String-keyed because a tenant-scoped grain is addressed by
// its composed {tenant}|{guid} key, not a bare Guid.
public interface IEmployeeOutboxProbe : IGrainWithStringKey
{
    Task ForceDrainViaReminderAsync();
    Task<int> GetPendingOutboxCountAsync();
}

public partial class EmployeeCommandHandler : EdictCommandHandler<EmployeeState>, IEmployeeOutboxProbe
{
    Task<EdictCommandResult> HandleAsync(AddEmployeeCommand command)
    {
        State.Department = command.Department;
        Raise(new EmployeeAddedEvent(command.EmployeeId, command.Department));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    public Task ForceDrainViaReminderAsync() =>
        ReceiveReminder("edict-outbox-drain", new TickStatus());

    public Task<int> GetPendingOutboxCountAsync() =>
        Task.FromResult(OutboxStateForProbe.Pending.Count);
}

// The tenant-as-partition List projection: each Employee event activates a grain keyed
// "{tenant}|{employeeGuid}", but the row is stored under the tenant alone, so a tenant's
// whole workforce lands in one partition. The partition is recovered from the tenant
// side of the grain's own composed key, the inverse of the fold that placed it there.
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
        CurrentRow.Department = edictEvent.Department;
        return Task.CompletedTask;
    }
}
