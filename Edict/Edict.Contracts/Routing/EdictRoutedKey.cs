using System.ComponentModel;

using Edict.Contracts.Tenancy;

namespace Edict.Contracts.Routing;

/// <summary>
/// The decomposition of a routed grain or stream key: the tenant the key belongs to
/// (<see langword="null"/> for a public aggregate) and the route Guid within that
/// tenant. Returned by <see cref="EdictKeyComposer.Parse"/>, the inverse of
/// <see cref="EdictKeyComposer.Compose"/>.
/// <para>
/// Public so framework code across the assembly boundary (the isolation call filter,
/// a tenant-as-partition projection) can read the recovered tenant; hidden from
/// consumer IntelliSense because no consumer parses a key by hand.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly record struct EdictRoutedKey(EdictTenantId? Tenant, Guid RouteKey);
