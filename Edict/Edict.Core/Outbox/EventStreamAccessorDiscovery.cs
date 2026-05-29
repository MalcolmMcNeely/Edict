using System.Reflection;

using Edict.Contracts.Routing;
using Edict.Core.Configuration;

using Microsoft.Extensions.Logging;

namespace Edict.Core.Outbox;

// Discovery uses the EdictEventStreamAccessor record from Edict.Contracts.Routing
// so a contracts-only assembly can emit a registrar (no Edict.Core dependency).
// Soft discovery: an assembly may carry commands without events (and vice
// versa), so a missing [EdictEventStreams] is not an error.
static class EventStreamAccessorDiscovery
{
    public static IDictionary<Type, EdictEventStreamAccessor> Discover(
        IEnumerable<Assembly> assemblies,
        ILogger logger)
    {
        var accessors = new Dictionary<Type, EdictEventStreamAccessor>();
        var origin = new Dictionary<Type, Assembly>();
        foreach (var asm in assemblies)
        {
            var attr = asm.GetCustomAttribute<EdictEventStreamsAttribute>();
            if (attr is null)
            {
                continue;
            }
            var contributed = new Dictionary<Type, EdictEventStreamAccessor>();
            var register = attr.RegistrarType.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)!;
            register.Invoke(null, [contributed]);
            foreach (var (eventType, accessor) in contributed)
            {
                if (origin.TryGetValue(eventType, out var firstAsm))
                {
                    throw new EdictWiringException(
                        $"Event '{eventType.FullName}' is registered by both " +
                        $"'{firstAsm.GetName().Name}' and '{asm.GetName().Name}'. " +
                        "An event type may only be declared in one assembly.");
                }
                accessors[eventType] = accessor;
                origin[eventType] = asm;
            }
        }
        if (accessors.Count == 0)
        {
            logger.LogWarning(
                "AddEdict() found no [assembly: EdictEventStreams] in the scanned assemblies. " +
                "Event publish will throw on the first raised event until an event-bearing assembly is referenced.");
        }
        return accessors;
    }
}
