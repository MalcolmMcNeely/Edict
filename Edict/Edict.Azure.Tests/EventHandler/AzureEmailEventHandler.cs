using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.Commands;
using Edict.Core.EventHandler;

using Orleans;
using Orleans.Streams;

namespace Edict.Azure.Tests.EventHandler;

// ── Domain events / commands ───────────────────────────────────────────────

// Handled event: the AzureEmailEventHandler has a matching Handle overload.
[EdictStream("AzureEmailEvents")]
public sealed partial record AzureCustomerNotifiedEvent(Guid CustomerId, string Reason) : EdictEvent
{
    [EdictRouteKey]
    public Guid CustomerId { get; init; } = CustomerId;

    public string Reason { get; init; } = Reason;
}

// Unhandled event: published onto "AzureEmailEvents" so the handler's implicit
// subscription delivers it, but no Handle overload exists → HandlesType
// returns false → the stream callback must be a pure no-op (no ring slot,
// no InvokeHandler entry staged).
[EdictStream("AzureUnhandledEvents")]
public sealed partial record AzureUnhandledEvent(Guid AggregateId, int Sequence) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;

    public int Sequence { get; init; } = Sequence;
}

// Command that drives the framework publish path so the span-stitch test
// observes a real "edict.event.publish AzureCustomerNotifiedEvent" span
// (a bare stream.OnNextAsync from the publisher grain bypasses the outbox
// publish executor and emits no publish span stitch is owned by
// the framework path, not by raw stream publishes).
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

// ── Publisher (bare stream → exercises stream callback path) ──────

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

// ── Event handler under test ───────────────────────────────────────────────

public interface IAzureEmailHandlerProbe : IGrainWithGuidKey
{
    Task<int> GetHandledCountAsync();
    Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync();
}

/// <summary>
/// Azure-suite <see cref="EdictEventHandler"/>: relies on the generator's
/// emitted <c>HandlesType</c> + <c>DispatchAsync</c> + implicit-stream
/// subscription, so this is the same shape consumers ship.
/// </summary>
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
