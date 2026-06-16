using Edict.Contracts.Commands;

namespace Edict.Core.Commands;

/// <summary>
/// Pure routing core: given a <see cref="EdictCommand"/> instance, returns the
/// aggregate grain interface and the Guid grain key, using the
/// generator-emitted command-to-grain map plus the generated <c>[EdictRouteKey]</c>
/// accessor. Deliberately has no Orleans dependency so it is unit-testable
/// without a TestCluster — the <see cref="EdictSender"/> shell owns the
/// Orleans hop.
/// </summary>
internal sealed class CommandRouteResolver(IReadOnlyDictionary<Type, CommandRoute> routes)
{
    /// <summary>
    /// Resolves the owning aggregate grain interface and its composed grain key for
    /// <paramref name="command"/>.
    /// </summary>
    public (Type GrainInterfaceType, string Key) Resolve(EdictCommand command)
    {
        var (grainInterfaceType, _, key) = ResolveTarget(command);
        return (grainInterfaceType, key);
    }

    /// <summary>
    /// Resolves the full Orleans addressing target — interface token, grain
    /// class name (for disambiguation across the shared
    /// <see cref="IEdictCommandHandler"/> interface) and composed grain key.
    /// </summary>
    (Type GrainInterfaceType, string GrainClassName, string Key) ResolveTarget(EdictCommand command)
    {
        var route = GetRoute(command);
        return (route.GrainInterfaceType, route.GrainClassName, route.RouteKeySelector(command));
    }

    /// <summary>Returns the full <see cref="CommandRoute"/> for <paramref name="command"/>.</summary>
    internal CommandRoute GetRoute(EdictCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return !routes.TryGetValue(command.GetType(), out var route) ? throw new EdictUnroutableCommandException(command.GetType()) : route;
    }

    /// <summary>
    /// Non-throwing lookup used on the silo side to write a command's
    /// <c>[EdictTelemeterized]</c> tags onto the handle span. Returns
    /// <see langword="false"/> for a command the route map does not contain so a
    /// missing entry degrades to an untagged span rather than a throw on the
    /// handle path.
    /// </summary>
    internal bool TryGetRoute(EdictCommand command, out CommandRoute? route) =>
        routes.TryGetValue(command.GetType(), out route);
}
