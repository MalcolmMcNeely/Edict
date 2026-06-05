using System.Reflection;

using Edict.Contracts.Routing;
using Edict.Core.Configuration;

using Microsoft.Extensions.Logging;

namespace Edict.Core.Projections;

// Soft discovery: an assembly may carry commands or events without projections,
// so a missing [EdictProjectionReadRoutes] is not an error. Mirrors
// EventStreamAccessorDiscovery — the generator does not run on Edict.Core, so
// the framework's own dead-letter projection route is hand-added by AddEdict.
static class ProjectionReadRouteDiscovery
{
    public static IDictionary<Type, string> Discover(IEnumerable<Assembly> assemblies, ILogger logger)
    {
        var routes = new Dictionary<Type, string>();
        var origin = new Dictionary<Type, Assembly>();
        foreach (var assembly in assemblies)
        {
            var attribute = assembly.GetCustomAttribute<EdictProjectionReadRoutesAttribute>();
            if (attribute is null)
            {
                continue;
            }
            var contributed = new Dictionary<Type, string>();
            var register = attribute.RegistrarType.GetMethod("Register", BindingFlags.Public | BindingFlags.Static)!;
            register.Invoke(null, [contributed]);
            foreach (var (rowType, grainClassName) in contributed)
            {
                if (origin.TryGetValue(rowType, out var firstAssembly))
                {
                    throw new EdictWiringException(
                        $"Read-model row '{rowType.FullName}' is owned by projections in both " +
                        $"'{firstAssembly.GetName().Name}' and '{assembly.GetName().Name}'. " +
                        "A row type may only be produced by one projection builder.");
                }
                routes[rowType] = grainClassName;
                origin[rowType] = assembly;
            }
        }
        return routes;
    }
}
