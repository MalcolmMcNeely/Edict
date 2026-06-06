using Edict.Contracts.Commands;
using Edict.Contracts.Events;
using Edict.Core.EventHandler;
using Edict.Core.Idempotency;

using Orleans;

namespace Edict.Core.Tests.Idempotency;

[EdictStream("EventHandlerDedupReasonWindow")]
public sealed partial record WindowReasonEvent(Guid AggregateId) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;
}

[EdictStream("EventHandlerDedupReasonInFlight")]
public sealed partial record InFlightReasonEvent(Guid AggregateId) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;
}

public interface IEventHandlerDedupReasonConsumer : IGrainWithGuidKey
{
    Task DeliverAsync(EdictEvent edictEvent);

    // Reserves the EventId's in-flight slot and leaves it held, so a following
    // DeliverAsync of the same id deterministically reaches the defense-in-depth
    // in_flight suppression branch without manufacturing concurrent delivery.
    Task ClaimInFlightAsync(Guid eventId);
}

// Hand-written EdictEventHandler consumer (no generator): overrides the two
// abstract seams directly so a white-box test can drive the staging path's dedup
// suppression through the real delivery entry and assert the duplicate counter.
public sealed class EventHandlerDedupReasonConsumer : EdictEventHandler, IEventHandlerDedupReasonConsumer
{
    protected override bool HandlesType(EdictEvent edictEvent) =>
        edictEvent is WindowReasonEvent or InFlightReasonEvent;

    protected override Task<EdictDispatchOutcome> DispatchAsync(EdictEvent edictEvent) =>
        Task.FromResult(HandlesType(edictEvent) ? EdictDispatchOutcome.HandledWithNoEffect : EdictDispatchOutcome.NotHandled);

    public Task DeliverAsync(EdictEvent edictEvent) => OnEdictEventAsync(edictEvent);

    public Task ClaimInFlightAsync(Guid eventId)
    {
        EnsureWindowInitialized();
        TryClaimInFlight(eventId);
        return Task.CompletedTask;
    }
}
