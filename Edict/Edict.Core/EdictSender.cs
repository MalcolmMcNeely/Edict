using Edict.Abstractions;

using Orleans;

namespace Edict.Core;

/// <summary>
/// The thin Orleans shell behind <see cref="IEdictSender"/>: resolve the route
/// (pure), get the aggregate grain by its Guid key, and dispatch. All routing
/// logic lives in <see cref="CommandRouteResolver"/>; this type only owns the
/// Orleans hop so the resolver stays cluster-free and unit-testable.
/// </summary>
public sealed class EdictSender(CommandRouteResolver resolver, IGrainFactory grainFactory)
    : IEdictSender
{
    /// <inheritdoc />
    public Task<CommandResult> Send(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var (_, grainClassName, key) = resolver.ResolveTarget(command);
        var grain = grainFactory.GetGrain<IEdictCommandHandler>(key, grainClassName);
        return grain.Dispatch(command);
    }
}
