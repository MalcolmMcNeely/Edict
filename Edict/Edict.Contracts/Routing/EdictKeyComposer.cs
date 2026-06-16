using System.ComponentModel;

using Edict.Contracts.Tenancy;

namespace Edict.Contracts.Routing;

/// <summary>
/// The one chokepoint every generator-emitted route-key site folds through to
/// build a grain or stream key: the command grain key, the event stream key, and
/// the projection/saga grain key that rides the stream all compose here rather
/// than each reinventing the fold. Lives in the contracts surface so the
/// stream-accessor registrar can be emitted into a contracts-only assembly.
/// <para>
/// Public so generated code can reference it; hidden from consumer IntelliSense
/// because no consumer composes a key by hand.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class EdictKeyComposer
{
    /// <summary>
    /// Folds a tenant and a stringified route key into the routed key. A public
    /// aggregate (and every key at this slice) composes to the bare route key;
    /// the <c>"{tenant}|{guid}"</c> fold for a tenant-scoped aggregate lands in a
    /// later slice behind this same seam.
    /// </summary>
    public static string Compose(EdictTenantId? tenant, string routeKey) => routeKey;
}
