using Edict.Contracts.Commands;
using Edict.Contracts.Results;
using Edict.Contracts.Sending;
using Edict.Core.Diagnostics;
using Edict.Core.Grains;

using Orleans;

namespace Edict.Core.Sending;

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
    public Task<EdictCommandResult> Send(EdictCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var route = resolver.GetRoute(command);
        var key = route.RouteKeySelector(command);
        var grain = grainFactory.GetGrain<IEdictCommandHandler>(key, route.GrainClassName);

        return CommandSpanScope.ExecuteAsync(
            $"edict.command {command.GetType().Name}",
            key,
            route.TagWriter,
            command,
            () => grain.Dispatch(command));
    }
}
