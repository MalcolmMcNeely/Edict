using Edict.Abstractions;

namespace Edict.Core;

/// <summary>
/// Pure routing core: given a <see cref="Command"/> instance, returns the
/// aggregate grain interface and the Guid grain key, using the
/// generator-emitted command-to-grain map plus the generated <c>[RouteKey]</c>
/// accessor. Deliberately has no Orleans dependency so it is unit-testable
/// without a TestCluster — the <see cref="EdictSender"/> shell owns the
/// Orleans hop.
/// </summary>
public sealed class CommandRouteResolver(IReadOnlyDictionary<Type, CommandRoute> routes)
{
    /// <summary>
    /// Resolves the owning aggregate grain interface and its Guid key for
    /// <paramref name="command"/>.
    /// </summary>
    public (Type GrainInterfaceType, Guid Key) Resolve(Command command)
    {
        var (grainInterfaceType, _, key) = ResolveTarget(command);
        return (grainInterfaceType, key);
    }

    /// <summary>
    /// Resolves the full Orleans addressing target — interface token, grain
    /// class name (for disambiguation across the shared
    /// <see cref="IEdictCommandHandler"/> interface) and Guid key.
    /// </summary>
    public (Type GrainInterfaceType, string GrainClassName, Guid Key) ResolveTarget(
        Command command)
    {
        var route = GetRoute(command);
        return (route.GrainInterfaceType, route.GrainClassName, route.RouteKeySelector(command));
    }

    /// <summary>Returns the full <see cref="CommandRoute"/> for <paramref name="command"/>.</summary>
    internal CommandRoute GetRoute(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!routes.TryGetValue(command.GetType(), out var route))
        {
            throw new UnroutableCommandException(command.GetType());
        }

        return route;
    }
}
