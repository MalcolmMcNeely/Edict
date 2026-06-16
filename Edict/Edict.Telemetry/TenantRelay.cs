using System.ComponentModel;

using Edict.Contracts.Tenancy;

using Orleans.Runtime;

namespace Edict.Telemetry;

/// <summary>
/// The per-turn tenant relay, sibling to <see cref="PrincipalRelay"/>. A grain
/// entry seeds the inbound message's tenant here — through
/// <see cref="OriginIdentity.Seed"/>, never alone — so an internal send issued
/// later in the same turn (a saga <c>Dispatch</c>, a schedule arm) stays inside the
/// same tenant wall without the durable field being threaded by hand. The relay is
/// per-turn only: the durable source re-seeded at every grain entry is the tenant
/// field on the wire, or the persisted schedule arm-context for a fire that runs
/// after a reactivation, across which <see cref="RequestContext"/> cannot survive.
/// <para>
/// Public so <c>Edict.Core</c> can read <see cref="Current"/> across the assembly
/// boundary; hidden from consumer IntelliSense because consumer code never reads
/// the relay. Seeding is internal so the only seam reachable for a grain entry is
/// <see cref="OriginIdentity.Seed"/>, which seeds the principal and the tenant
/// together.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class TenantRelay
{
    // Seeds the current turn's tenant from the durable source. A null tenant clears
    // the slot so a turn that inherited nothing does not leak a stale tenant onto a
    // later send. Internal: callers seed through OriginIdentity.Seed.
    internal static void Seed(EdictTenantId? tenant)
    {
        if (tenant is { } wall)
        {
            RequestContext.Set(EdictDiagnostics.TenantKey, wall.Value);
        }
        else
        {
            RequestContext.Remove(EdictDiagnostics.TenantKey);
        }
    }

    /// <summary>The tenant seeded for the current turn, or <see langword="null"/> when none was.</summary>
    public static EdictTenantId? Current() =>
        RequestContext.Get(EdictDiagnostics.TenantKey) is string wall
            ? EdictTenantId.Of(wall)
            : null;
}
