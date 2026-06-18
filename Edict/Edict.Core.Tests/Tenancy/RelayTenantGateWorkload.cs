using System.Collections.Concurrent;

using Edict.Contracts.Commands;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Events;
using Edict.Contracts.Persistence;
using Edict.Contracts.Routing;
using Edict.Contracts.Tenancy;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Sagas;

using MessagePack;

using Orleans;
using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.Tests.Tenancy;

// A typed, tenant-scoped route key for the relayed-gate workload, distinct from the
// EmployeeId key the composition tests use so the generator wires a dedicated route.
[EdictTenantScoped]
[MessagePackObject(keyAsPropertyName: true)]
public readonly record struct WalledTargetId(Guid Value);

// Dispatched into a tenant-scoped aggregate. Carries the relayed tenant inherited
// from the handled trigger event — null on a public-origin chain.
public sealed partial record DispatchIntoWalledTarget(WalledTargetId TargetId) : EdictCommand
{
    [EdictRouteKey]
    public WalledTargetId TargetId { get; init; } = TargetId;
}

// Dispatched into a public aggregate: its route-key type carries no wall, so a null
// relayed tenant composes a bare key and the gate stays out of the way.
public sealed partial record DispatchIntoPublicTarget(Guid TargetId) : EdictCommand
{
    [EdictRouteKey]
    public Guid TargetId { get; init; } = TargetId;
}

[EdictStream("RelayTenantGateWorkflow")]
public sealed partial record CrossIntoWalledTrigger(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

[EdictStream("RelayTenantGateWorkflow")]
public sealed partial record CrossIntoPublicTrigger(Guid WorkflowId) : EdictEvent
{
    [EdictRouteKey]
    public Guid WorkflowId { get; init; } = WorkflowId;
}

[GenerateSerializer]
[Alias("Edict.Core.Tests.Tenancy.RelayGateProgress")]
public sealed class RelayGateProgress : IEdictPersistedState
{
    [Id(0)]
    public int Handled { get; set; }
}

// A saga handling trigger events off a shared stream: each one dispatches into a
// target aggregate, inheriting the trigger's tenant through the relay. A walled
// trigger carrying no tenant is the public-to-tenant crossing the gate must refuse.
public partial class RelayTenantGateSaga : EdictSaga<RelayGateProgress>
{
    Task HandleAsync(CrossIntoWalledTrigger edictEvent)
    {
        Progress.Handled++;
        Dispatch(new DispatchIntoWalledTarget(new WalledTargetId(edictEvent.WorkflowId)));
        return Task.CompletedTask;
    }

    Task HandleAsync(CrossIntoPublicTrigger edictEvent)
    {
        Progress.Handled++;
        Dispatch(new DispatchIntoPublicTarget(edictEvent.WorkflowId));
        return Task.CompletedTask;
    }
}

// Records every command that actually reaches a target handler. A bare key landing
// in the default partition would show up here; the gate keeping the tenant-less
// walled dispatch out of the queue is exactly the behaviour under test.
public partial class WalledTargetCommandHandler : EdictCommandHandler
{
    Task<EdictCommandResult> HandleAsync(DispatchIntoWalledTarget command)
    {
        RelayTenantGateCaptures.WalledTargetReceived.Enqueue(command);
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

public partial class PublicTargetCommandHandler : EdictCommandHandler
{
    Task<EdictCommandResult> HandleAsync(DispatchIntoPublicTarget command)
    {
        RelayTenantGateCaptures.PublicTargetReceived.Enqueue(command);
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

// Pushes an event directly onto the saga's stream so a test can inject a trigger with
// a chosen tenant (or none) without routing a command through the origin send paths.
public interface IRelayGatePublisher : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent edictEvent);
}

public sealed class RelayGatePublisher : Grain, IRelayGatePublisher
{
    public Task PublishAsync(EdictEvent edictEvent)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("RelayTenantGateWorkflow", this.GetPrimaryKey()));
        return stream.OnNextAsync(edictEvent);
    }
}

// Process-static sinks for the in-memory cluster: the handlers and the dead-letter
// capture run in this process, so a test reads back what landed and what was refused.
static class RelayTenantGateCaptures
{
    public static readonly ConcurrentQueue<DispatchIntoWalledTarget> WalledTargetReceived = new();
    public static readonly ConcurrentQueue<DispatchIntoPublicTarget> PublicTargetReceived = new();
    public static readonly ConcurrentQueue<EdictDeadLetterRaised> DeadLetters = new();
}

// Replaces the real PublishEventExecutor so the EdictDeadLetterRaised the outbox
// stages when the gate refuses a relayed send is captured directly, table-free. The
// real SendCommandExecutor stays in place so the gate fires on the relayed dispatch.
sealed class RelayGateDeadLetterCapturingExecutor(Serializer serializer) : IOutboxEffectExecutor
{
    public OutboxEffectKind Kind => OutboxEffectKind.PublishEvent;

    public Task<OutboxEntry?> ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch,
        Type? consumerType,
        EdictEvent? liveWireEvent)
    {
        var edictEvent = liveWireEvent ?? serializer.Deserialize<EdictEvent>(entry.Payload);
        if (edictEvent is EdictDeadLetterRaised raised)
        {
            RelayTenantGateCaptures.DeadLetters.Enqueue(raised);
        }
        return Task.FromResult<OutboxEntry?>(null);
    }
}
