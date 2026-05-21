using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.Commands;
using Edict.Core.EventHandler;

using Orleans;
using Orleans.Streams;

namespace Edict.Azure.Tests.EventHandler;

[EdictStream("AzureEmailEvents")]
public sealed partial record AzureCustomerNotifiedEvent(Guid CustomerId, string Reason) : EdictEvent
{
    [EdictRouteKey]
    public Guid CustomerId { get; init; } = CustomerId;

    public string Reason { get; init; } = Reason;
}

// No Handle overload exists for this event — HandlesType returns false, so
// the stream callback must be a pure no-op (no ring slot, no InvokeHandler
// entry staged) when the implicit subscription delivers it.
[EdictStream("AzureUnhandledEvents")]
public sealed partial record AzureUnhandledEvent(Guid AggregateId, int Sequence) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;

    public int Sequence { get; init; } = Sequence;
}

// Drives the framework publish path so the span-stitch test observes a real
// edict.event.publish span — a bare stream.OnNextAsync from the publisher
// grain bypasses the outbox executor and emits no publish span.
public sealed partial record AzureNotifyCustomerCommand(Guid CustomerId, string Reason) : EdictCommand
{
    [EdictRouteKey]
    public Guid CustomerId { get; init; } = CustomerId;

    public string Reason { get; init; } = Reason;
}

public partial class AzureCustomerNotificationCommandHandler : EdictCommandHandler
{
    public Task<EdictCommandResult> Handle(AzureNotifyCustomerCommand command)
    {
        Raise(new AzureCustomerNotifiedEvent(command.CustomerId, command.Reason));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

public interface IAzureEmailEventPublisher : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent evt);
}

public sealed class AzureEmailEventPublisher : Grain, IAzureEmailEventPublisher
{
    public Task PublishAsync(EdictEvent evt)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("AzureEmailEvents", this.GetPrimaryKey()));
        return stream.OnNextAsync(evt);
    }
}

public interface IAzureEmailHandlerProbe : IGrainWithGuidKey
{
    Task<int> GetHandledCountAsync();
    Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync();
}

public sealed partial class AzureEmailEventHandler : EdictEventHandler, IAzureEmailHandlerProbe
{
    readonly List<Guid> _handled = [];

    public Task Handle(AzureCustomerNotifiedEvent evt)
    {
        _handled.Add(evt.EventId);
        return Task.CompletedTask;
    }

    public Task<int> GetHandledCountAsync() => Task.FromResult(_handled.Count);

    public Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync() =>
        Task.FromResult<IReadOnlyList<Guid>>(_handled.AsReadOnly());
}
