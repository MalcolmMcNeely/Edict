using System.Collections.Frozen;
using System.Diagnostics;

using Edict.Contracts.Events;

namespace Edict.Telemetry;

/// <summary>
/// Frozen-dictionary <see cref="IEventTagWriters"/>. Concrete events with at
/// least one <c>[EdictTelemeterized]</c> property contribute one entry; events
/// without contribute none, so <see cref="TryGet"/> returns <c>false</c> and
/// the caller pays no extra cost.
/// </summary>
public sealed class EventTagWriters(IReadOnlyDictionary<Type, Action<EdictEvent, Activity>> writers)
    : IEventTagWriters
{
    readonly FrozenDictionary<Type, Action<EdictEvent, Activity>> _writers =
        writers.ToFrozenDictionary();

    public bool TryGet(Type eventType, out Action<EdictEvent, Activity> writer)
    {
        if (_writers.TryGetValue(eventType, out var found))
        {
            writer = found;
            return true;
        }
        writer = static (_, _) => { };
        return false;
    }
}
