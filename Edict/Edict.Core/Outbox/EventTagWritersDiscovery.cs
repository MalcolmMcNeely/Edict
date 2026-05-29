using System.Diagnostics;
using System.Reflection;

using Edict.Contracts.Events;
using Edict.Contracts.Routing;
using Edict.Core.Configuration;

using Microsoft.Extensions.Logging;

namespace Edict.Core.Outbox;

// Mirrors EventStreamAccessorDiscovery: walks [assembly: EdictEventTagWriters]
// to stitch the per-event tag-writer dictionary at AddEdict() time. Events
// without [EdictTelemeterized] properties contribute no entry, so TryGet
// returns false and the executor pays no cost.
static class EventTagWritersDiscovery
{
    public static IDictionary<Type, Action<EdictEvent, Activity>> Discover(
        IEnumerable<Assembly> assemblies,
        ILogger logger)
    {
        var writers = new Dictionary<Type, Action<EdictEvent, Activity>>();
        var origin = new Dictionary<Type, Assembly>();
        foreach (var asm in assemblies)
        {
            var attr = asm.GetCustomAttribute<EdictEventTagWritersAttribute>();
            if (attr is null)
            {
                continue;
            }
            var contributed = new Dictionary<Type, Action<EdictEvent, Activity>>();
            var register = attr.RegistrarType.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)!;
            register.Invoke(null, [contributed]);
            foreach (var (eventType, writer) in contributed)
            {
                if (origin.TryGetValue(eventType, out var firstAsm))
                {
                    throw new EdictWiringException(
                        $"Event '{eventType.FullName}' has a tag writer registered by both " +
                        $"'{firstAsm.GetName().Name}' and '{asm.GetName().Name}'. " +
                        "An event type may only be declared in one assembly.");
                }
                writers[eventType] = writer;
                origin[eventType] = asm;
            }
        }
        if (writers.Count == 0)
        {
            logger.LogDebug(
                "AddEdict() found no [assembly: EdictEventTagWriters] in the scanned assemblies. " +
                "No events carry [EdictTelemeterized] — IEventTagWriters.TryGet will return false for every type.");
        }
        return writers;
    }
}
