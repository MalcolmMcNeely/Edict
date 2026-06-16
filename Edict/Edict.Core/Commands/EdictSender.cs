using System.ComponentModel;
using System.Diagnostics;
using Edict.Contracts.Audit;
using Edict.Contracts.Commands;
using Edict.Contracts.Routing;
using Edict.Contracts.Sending;
using Edict.Contracts.Tenancy;
using Edict.Core.Audit;
using Edict.Core.Tenancy;
using Edict.Telemetry;

namespace Edict.Core.Commands;

/// <summary>
/// The thin Orleans shell behind <see cref="IEdictSender"/>: resolve the route
/// (pure), get the aggregate grain by its Guid key, and dispatch. All routing
/// logic lives in <see cref="CommandRouteResolver"/>; this type only owns the
/// Orleans hop so the resolver stays cluster-free and unit-testable.
/// <para>
/// Public so the source generator can emit <c>is EdictSender</c> casts in
/// consumer-emitted interceptor stubs; hidden from consumer IntelliSense
/// because consumer code resolves <see cref="IEdictSender"/>, never this class.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class EdictSender : IEdictSender
{
    readonly CommandRouteResolver _resolver;
    readonly IGrainFactory _grainFactory;
    readonly EdictPrincipalStamper _principalStamper;
    readonly EdictTenantStamper _tenantStamper;

    internal EdictSender(
        CommandRouteResolver resolver,
        IGrainFactory grainFactory,
        EdictPrincipalStamper principalStamper,
        EdictTenantStamper tenantStamper)
    {
        _resolver = resolver;
        _grainFactory = grainFactory;
        _principalStamper = principalStamper;
        _tenantStamper = tenantStamper;
    }

    /// <inheritdoc />
    public async Task<EdictCommandResult> SendAsync(EdictCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        command = EnsureCorrelation(command);
        command = _principalStamper.StampAtOrigin(command);
        command = _tenantStamper.StampAtOrigin(command);
        var route = _resolver.GetRoute(command);
        var key = route.RouteKeySelector(command);
        var grain = _grainFactory.GetGrain<IEdictCommandHandler>(key, route.GrainClassName);

        using var activity = EdictDiagnostics.ActivitySource.StartEdictCommand($"{SemanticConventions.Commands.Spans.Command} {command.GetType().Name}");

        if (activity is not null)
        {
            activity.SetEdictCommandTags(key);
            route.TagWriter?.Invoke(command, activity);
            // On a saga's outbox dispatch the edict.command.send span is already the
            // cross-turn link carrier in RequestContext; leave it in place so the
            // receiving handle links to the send rather than to this intervening span.
            if (!ActivityExtensions.IsCrossTurnLink())
            {
                activity.CaptureToRequestContext();
            }
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

    /// <inheritdoc />
    public Task<EdictCommandResult> SendAsync(EdictCommand command, EdictPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Stamp before the resolver runs: StampAtOrigin honours an already-present
        // principal, so the explicit value wins and the fail-closed gate is never
        // reached for a context-free origin.
        return SendAsync(command with { Principal = principal });
    }

    /// <inheritdoc />
    public Task<EdictCommandResult> SendAsync(EdictCommand command, EdictTenantId tenant)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Stamp before the resolver runs: StampAtOrigin honours an already-present
        // tenant, so the explicit value wins and the fail-closed gate is never
        // reached for an establishing crossing or context-free origin.
        return SendAsync(command with { Tenant = tenant });
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
    [EditorBrowsable(EditorBrowsableState.Never)]
    public async Task<EdictCommandResult> SendFastPathAsync<TCommand>(
        TCommand command,
        string routeKey,
        string commandSimpleName,
        string grainClassName,
        Action<TCommand, Activity>? extraTags)
        where TCommand : EdictCommand
    {
        ArgumentNullException.ThrowIfNull(command);

        command = EnsureCorrelation(command);
        command = _principalStamper.StampAtOrigin(command);
        command = _tenantStamper.StampAtOrigin(command);
        // Fold the tenant in after stamping so a tenant-scoped command reaches the
        // wall its resolved tenant names, not the default key space.
        var key = EdictKeyComposer.Compose(command.Tenant, routeKey);
        var grain = _grainFactory.GetGrain<IEdictCommandHandler>(key, grainClassName);

        using var activity = EdictDiagnostics.ActivitySource.StartEdictCommand($"{SemanticConventions.Commands.Spans.Command} {commandSimpleName}");

        if (activity is not null)
        {
            activity.SetEdictCommandTags(key);
            extraTags?.Invoke(command, activity);
            if (!ActivityExtensions.IsCrossTurnLink())
            {
                activity.CaptureToRequestContext();
            }
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

    // Mint the chain-stable correlation id here, at the boundary, when the command
    // carries none, and honour a caller-supplied value (idempotency-key style).
    // Assign-once at the boundary rather than a new()-time initializer on the
    // record, mirroring how EventId is minted as an event enters the outbox; the
    // minted command is what dispatches, so the correlation is present when the
    // handler runs and propagates onto every event it raises.
    static TCommand EnsureCorrelation<TCommand>(TCommand command) where TCommand : EdictCommand =>
        command.CorrelationId == Guid.Empty ? command with { CorrelationId = Guid.NewGuid() } : command;
}
