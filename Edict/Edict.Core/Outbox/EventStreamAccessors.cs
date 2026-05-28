using Edict.Contracts.Events;
using Edict.Contracts.Routing;

namespace Edict.Core.Outbox;

/// <summary>
/// Pure routing core: given an <see cref="EdictEvent"/> instance, returns its
/// domain stream and route key from a generator-emitted accessor map. No
/// Orleans dependency; unit-testable without a cluster. Mirrors
/// <c>CommandRouteResolver</c> on the command side.
/// </summary>
public sealed class EventStreamAccessors(IReadOnlyDictionary<Type, EdictEventStreamAccessor> accessors)
    : IEventStreamAccessors
{
    public (string StreamName, Guid RouteKey) Resolve(EdictEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (!accessors.TryGetValue(evt.GetType(), out var accessor))
        {
            throw new InvalidOperationException(
                $"Event '{evt.GetType().FullName}' has no registered EdictEventStreamAccessor. "
                + "Ensure the concrete EdictEvent carries [EdictStream] and exactly one [EdictRouteKey] Guid property, "
                + "and that its declaring assembly is scanned by AddEdict().");
        }

        return (accessor.StreamName, accessor.RouteKeyGetter(evt));
    }
}
