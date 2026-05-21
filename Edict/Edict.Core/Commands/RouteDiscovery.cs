using System.Reflection;

using Edict.Contracts.Routing;

using Microsoft.Extensions.Logging;

namespace Edict.Core.Commands;

static class RouteDiscovery
{
    public static IDictionary<Type, CommandRoute> Discover(
        IEnumerable<Assembly> assemblies,
        bool requireAttribute,
        ILogger logger)
    {
        var routes = new Dictionary<Type, CommandRoute>();
        var origin = new Dictionary<Type, Assembly>();
        foreach (var asm in assemblies)
        {
            var attr = asm.GetCustomAttribute<EdictRoutesAttribute>();
            if (attr is null)
            {
                if (requireAttribute)
                {
                    throw new InvalidOperationException(
                        $"Assembly '{asm.GetName().Name}' was passed to AddEdict(params Assembly[]) " +
                        $"but carries no [assembly: {nameof(EdictRoutesAttribute)}]. " +
                        "Either remove it from the call or reference Edict.Generators from that project so the registrar is emitted.");
                }
                continue;
            }
            var contributed = new Dictionary<Type, CommandRoute>();
            var register = attr.RegistrarType.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)!;
            register.Invoke(null, [contributed]);
            foreach (var (commandType, route) in contributed)
            {
                if (origin.TryGetValue(commandType, out var firstAsm))
                {
                    throw new InvalidOperationException(
                        $"Command '{commandType.FullName}' is registered by both " +
                        $"'{firstAsm.GetName().Name}' and '{asm.GetName().Name}'. " +
                        "A command type may only be handled by one assembly.");
                }
                routes[commandType] = route;
                origin[commandType] = asm;
            }
        }
        if (routes.Count == 0)
        {
            logger.LogWarning(
                "AddEdict() found no [assembly: EdictRoutes] in the scanned assemblies. " +
                "Send() will throw UnroutableCommandException until a handler-bearing assembly is referenced.");
        }
        return routes;
    }
}
