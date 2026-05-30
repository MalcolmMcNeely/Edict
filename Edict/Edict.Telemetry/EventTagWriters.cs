using System.Collections.Frozen;
using System.Diagnostics;

using Edict.Contracts.Events;

namespace Edict.Telemetry;

sealed class EventTagWriters(IReadOnlyDictionary<Type, Action<EdictEvent, Activity>> writers)
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
