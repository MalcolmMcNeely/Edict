using Edict.Contracts.Events;

namespace Edict.Core.Grains;

public abstract class ProjectionBuilderGrain : EventDeduplicationGrain
{
    /// <summary>
    /// Called by the generated <c>DispatchAsync</c> for each matched event type.
    /// The default passes the event directly to <paramref name="handler"/>.
    /// <c>TableProjectionBuilderGrain&lt;T&gt;</c> overrides to wrap with
    /// load-apply-writeback around the handler call (ADR 0012).
    /// </summary>
    protected virtual Task DispatchEventAsync<TEvent>(TEvent evt, Func<TEvent, Task> handler)
        where TEvent : Event
        => handler(evt);
}
