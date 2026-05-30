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
    /// Resolves the owning aggregate grain interface and its Guid key for
    /// <paramref name="command"/>.
    /// </summary>
    public (Type GrainInterfaceType, Guid Key) Resolve(EdictCommand command)
    {
        var (grainInterfaceType, _, key) = ResolveTarget(command);
        return (grainInterfaceType, key);
    }

    /// <summary>
    /// Resolves the full Orleans addressing target — interface token, grain
    /// class name (for disambiguation across the shared
    /// <see cref="IEdictCommandHandler"/> interface) and Guid key.
    /// </summary>
    (Type GrainInterfaceType, string GrainClassName, Guid Key) ResolveTarget(EdictCommand command)
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
}
