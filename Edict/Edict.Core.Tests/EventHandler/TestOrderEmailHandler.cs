using Edict.Contracts.Events;
using Edict.Core.EventHandler;

using Orleans;

namespace Edict.Core.Tests.EventHandler;

public interface IOrderEmailHandlerProbe : IGrainWithGuidKey
{
    Task<int> GetHandledCountAsync();
}

/// <summary>
/// Test <see cref="EdictEventHandler"/> consumer used by Core.Tests for the
/// stream-callback / FIFO / dead-letter and dedup integration tests (ADR 0023).
/// The generator emits the Orleans grain interface <c>ITestOrderEmailHandler</c>,
/// the <c>[ImplicitStreamSubscription("Orders")]</c> attribute, the synchronous
/// <c>HandlesType</c> pre-flight, and the async <c>DispatchAsync</c> over the
/// event types this class has a <c>Handle</c> overload for — same shape
/// consumers ship in their own assemblies. The probe interface
/// <see cref="IOrderEmailHandlerProbe"/> is hand-rolled so tests can read
/// observable state without going through the generator-emitted surface.
/// </summary>
public sealed partial class TestOrderEmailHandler : EdictEventHandler, IOrderEmailHandlerProbe
{
    readonly List<EdictEvent> _handled = [];

    public Task Handle(OrderPlacedEvent evt)
    {
        _handled.Add(evt);
        return Task.CompletedTask;
    }

    public Task<int> GetHandledCountAsync() => Task.FromResult(_handled.Count);
}
