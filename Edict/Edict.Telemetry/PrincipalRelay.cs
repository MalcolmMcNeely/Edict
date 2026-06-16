using System.ComponentModel;

using Edict.Contracts.Audit;

using Orleans.Runtime;

namespace Edict.Telemetry;

/// <summary>
/// The per-turn principal relay, sibling to <see cref="ActivityExtensions.CaptureToRequestContext"/>.
/// A grain entry seeds the inbound message's principal here so an internal send
/// issued later in the same turn — a saga <c>Dispatch</c>, a schedule arm — inherits
/// the actor without the durable field being threaded by hand. The relay is
/// per-turn only: the durable source re-seeded at every grain entry is the
/// principal field on the wire, or the persisted schedule arm-context for a fire
/// that runs after a reactivation, across which <see cref="RequestContext"/> cannot
/// survive.
/// <para>
/// Public so <c>Edict.Core</c> can reach it across the assembly boundary, exactly
/// as it reaches <see cref="ActivityExtensions"/>; hidden from consumer IntelliSense
/// because consumer code never seeds or reads the relay.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class PrincipalRelay
{
    // Seeds the current turn's principal from the durable source. A null principal
    // clears the slot so a turn that inherited nothing does not leak a stale actor
    // onto a later send. Internal: callers seed through OriginIdentity.Seed, which
    // seeds the principal and the tenant together so neither can drift.
    internal static void Seed(EdictPrincipal? principal)
    {
        if (principal is { } actor)
        {
            RequestContext.Set(EdictDiagnostics.PrincipalKey, actor.Value);
        }
        else
        {
            RequestContext.Remove(EdictDiagnostics.PrincipalKey);
        }
    }

    /// <summary>The principal seeded for the current turn, or <see langword="null"/> when none was.</summary>
    public static EdictPrincipal? Current() =>
        RequestContext.Get(EdictDiagnostics.PrincipalKey) is string actor
            ? EdictPrincipal.Of(actor)
            : null;
}
