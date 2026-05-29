using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.Commands;
using Edict.Core.EventHandler;

using Orleans;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Tests.Conformance.EventHandler;

[EdictStream("ConformanceEmailEvents")]
public sealed partial record CustomerNotifiedEvent(Guid CustomerId, string Reason) : EdictEvent
{
    [EdictRouteKey]
    public Guid CustomerId { get; init; } = CustomerId;

    [Edict.Contracts.Telemetry.EdictTelemeterized]
    public string Reason { get; init; } = Reason;
}

// No Handle overload exists for this event — HandlesType returns false, so
// the stream callback must be a pure no-op (no ring slot, no InvokeHandler
// entry staged) when the implicit subscription delivers it.
[EdictStream("ConformanceUnhandledEvents")]
public sealed partial record UnhandledEmailEvent(Guid AggregateId, int Sequence) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;

    public int Sequence { get; init; } = Sequence;
}

// Drives the framework publish path so the span-stitch test observes a real
// edict.event.publish span — a bare stream.OnNextAsync from the publisher
// grain bypasses the outbox executor and emits no publish span.
public sealed partial record NotifyCustomerCommand(Guid CustomerId, string Reason) : EdictCommand
{
    [EdictRouteKey]
    public Guid CustomerId { get; init; } = CustomerId;

    public string Reason { get; init; } = Reason;
}

public partial class CustomerNotificationCommandHandler : EdictCommandHandler
{
    public Task<EdictCommandResult> Handle(NotifyCustomerCommand command)
    {
        Raise(new CustomerNotifiedEvent(command.CustomerId, command.Reason));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }
}

public interface IEmailEventPublisher : IGrainWithGuidKey
{
    Task PublishAsync(EdictEvent edictEvent);
}

public sealed class EmailEventPublisher : Grain, IEmailEventPublisher
{
    public Task PublishAsync(EdictEvent edictEvent)
    {
        var stream = this.GetStreamProvider("edict")
            .GetStream<EdictEvent>(StreamId.Create("ConformanceEmailEvents", this.GetPrimaryKey()));
        return stream.OnNextAsync(edictEvent);
    }
}

public interface IEmailHandlerProbe : IGrainWithGuidKey
{
    Task<int> GetHandledCountAsync();
    Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync();
}

public sealed partial class EmailEventHandler : EdictEventHandler, IEmailHandlerProbe
{
    readonly List<Guid> _handled = [];

    public Task Handle(CustomerNotifiedEvent edictEvent)
    {
        _handled.Add(edictEvent.EventId);
        return Task.CompletedTask;
    }

    public Task<int> GetHandledCountAsync() => Task.FromResult(_handled.Count);

    public Task<IReadOnlyList<Guid>> GetHandledEventIdsAsync() =>
        Task.FromResult<IReadOnlyList<Guid>>(_handled.AsReadOnly());
}

static class EmailHandlerWaiters
{
    public static async Task<IReadOnlyList<Guid>> WaitForHandledAsync(
        IEmailHandlerProbe handler, int expectedCount = 1, int timeoutSeconds = 15)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var ids = await handler.GetHandledEventIdsAsync();
            if (ids.Count >= expectedCount)
            {
                return ids;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
        return await handler.GetHandledEventIdsAsync();
    }
}
