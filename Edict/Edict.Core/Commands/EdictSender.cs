using System.Diagnostics;
using Edict.Contracts.Commands;
using Edict.Contracts.Sending;
using Edict.Telemetry;

namespace Edict.Core.Commands;

/// <summary>
/// The thin Orleans shell behind <see cref="IEdictSender"/>: resolve the route
/// (pure), get the aggregate grain by its Guid key, and dispatch. All routing
/// logic lives in <see cref="CommandRouteResolver"/>; this type only owns the
/// Orleans hop so the resolver stays cluster-free and unit-testable.
/// </summary>
public sealed class EdictSender : IEdictSender
{
    readonly CommandRouteResolver _resolver;
    readonly IGrainFactory _grainFactory;

    internal EdictSender(CommandRouteResolver resolver, IGrainFactory grainFactory)
    {
        _resolver = resolver;
        _grainFactory = grainFactory;
    }

    /// <inheritdoc />
    public async Task<EdictCommandResult> Send(EdictCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var route = _resolver.GetRoute(command);
        var key = route.RouteKeySelector(command);
        var grain = _grainFactory.GetGrain<IEdictCommandHandler>(key, route.GrainClassName);

        using var activity = EdictDiagnostics.ActivitySource.StartEdictCommand($"{SemanticConventions.Commands.Spans.Command} {command.GetType().Name}");

        if (activity is not null)
        {
            activity.SetEdictCommandTags(key);
            route.TagWriter?.Invoke(command, activity);
            activity.CaptureToRequestContext();
        }

        try
        {
            return await grain.DispatchAsync(command).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            throw;
        }
    }

    /// <summary>
    /// Generator-only fast path called by the per-type Send interceptor stubs.
    /// Skips the <see cref="CommandRouteResolver"/> dictionary lookup and the
    /// route-key/tag delegate hops by accepting the typed command, the
    /// already-extracted route key, the known command simple name, and the
    /// grain class name (still needed for Orleans' shared
    /// <see cref="IEdictCommandHandler"/> interface — the per-grain typed
    /// interface would force every consumer to ship a <c>[GrainType]</c>
    /// binding, which the framework deliberately avoids).
    /// <paramref name="extraTags"/> is a non-capturing <c>static</c> lambda
    /// for telemeterized property writes — zero per-call allocation. Not a
    /// stable public API; the interceptor emitter is the only caller.
    /// </summary>
    public async Task<EdictCommandResult> SendFastPathAsync<TCommand>(
        TCommand command,
        Guid routeKey,
        string commandSimpleName,
        string grainClassName,
        Action<TCommand, Activity>? extraTags)
        where TCommand : EdictCommand
    {
        ArgumentNullException.ThrowIfNull(command);

        var grain = _grainFactory.GetGrain<IEdictCommandHandler>(routeKey, grainClassName);

        using var activity = EdictDiagnostics.ActivitySource.StartEdictCommand($"{SemanticConventions.Commands.Spans.Command} {commandSimpleName}");

        if (activity is not null)
        {
            activity.SetEdictCommandTags(routeKey);
            extraTags?.Invoke(command, activity);
            activity.CaptureToRequestContext();
        }

        try
        {
            return await grain.DispatchAsync(command).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            throw;
        }
    }
}
