using Edict.Contracts.Events;
using Edict.Core.Idempotency;

namespace Edict.Core.Projections;

public abstract class EdictProjectionBuilder : EdictIdempotencyBase
{
    /// <summary>
    /// Called by the generated <c>DispatchAsync</c> for each matched event type.
    /// The default passes the event directly to <paramref name="handler"/>.
    /// <c>EdictTableProjectionBuilder&lt;T&gt;</c> overrides to wrap with
    /// load-apply-writeback around the handler call (ADR 0012).
    /// </summary>
    protected virtual Task DispatchEventAsync<TEvent>(TEvent evt, Func<TEvent, Task> handler)
        where TEvent : EdictEvent
        => handler(evt);
}
