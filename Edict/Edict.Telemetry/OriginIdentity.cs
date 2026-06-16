using System.ComponentModel;

using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;

namespace Edict.Telemetry;

/// <summary>
/// The one seam that seeds the per-turn identity relays. Every grain entry that
/// seeds the inbound message's identity calls <see cref="Seed"/>, which seeds the
/// principal and the tenant together. Seeding one without the other is
/// unrepresentable here by construction: a forgotten tenant seed would silently
/// route a later same-turn send into the wrong tenant wall, a cross-tenant leak,
/// so the two identities share a single seeding call rather than two that can
/// drift apart. The relays themselves keep separate <c>Current()</c> reads, since
/// the principal and the tenant are independent axes once seeded.
/// <para>
/// Public so <c>Edict.Core</c> can reach it across the assembly boundary; hidden
/// from consumer IntelliSense because consumer code never seeds the relays.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class OriginIdentity
{
    /// <summary>
    /// Seeds the current turn's principal and tenant from the durable source. A
    /// <see langword="null"/> on either axis clears that slot, so a turn that
    /// inherited nothing does not leak a stale identity onto a later send.
    /// </summary>
    public static void Seed(EdictPrincipal? principal, EdictTenantId? tenant)
    {
        PrincipalRelay.Seed(principal);
        TenantRelay.Seed(tenant);
    }
}
