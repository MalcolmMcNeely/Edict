using System.ComponentModel;

using Orleans.Runtime;

namespace Edict.Telemetry;

/// <summary>
/// The per-call authorization for a deliberate cross-tenant call. The relay never
/// changes tenancy implicitly, so a call whose composed key names a different tenant
/// than the turn's ambient one is denied by the isolation filter by default. The two
/// sanctioned crossings — the explicit establishing-crossing send overload and the
/// privileged operator console — call <see cref="Authorize"/> to mark the call as
/// intended; the filter then honours it and records the crossing rather than denying.
/// The flag rides <see cref="RequestContext"/> so it reaches the target grain across
/// the same-process hop, exactly as the relay does.
/// <para>
/// Public so <c>Edict.Core</c> can set and read it across the assembly boundary;
/// hidden from consumer IntelliSense because consumer code never authorizes a crossing
/// by hand — it goes through the send overload or the operator path.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class TenantCrossing
{
    /// <summary>Marks the current call as a deliberate, authorized cross-tenant crossing.</summary>
    public static void Authorize() => RequestContext.Set(EdictDiagnostics.CrossTenantAuthorizedKey, true);

    /// <summary>Whether the current call was authorized to cross the tenant wall.</summary>
    public static bool IsAuthorized() => RequestContext.Get(EdictDiagnostics.CrossTenantAuthorizedKey) is true;
}
