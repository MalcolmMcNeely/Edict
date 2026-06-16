namespace Edict.Core.Tenancy;

/// <summary>
/// Thrown synchronously at an originating <c>SendAsync</c> when tenancy is on but
/// the edge resolver yields no tenant, before the command is dispatched or
/// anything is persisted. A tenant-scoped command routed without a tenant would
/// fall into the default key space, a silent cross-tenant leak, so the send is
/// refused at the edge rather than completed under the wrong wall. A context-free
/// origin (a public-to-tenant establishing crossing, a worker, an import) supplies
/// a tenant explicitly via <c>SendAsync(command, tenant)</c> instead.
/// </summary>
public sealed class EdictMissingTenantException : Exception
{
    public EdictMissingTenantException(Type commandType)
        : base($"Tenancy is on but no tenant resolved for '{commandType.Name}' at an originating SendAsync. "
            + "The edge resolver registered via AddEdictTenant returned null. Supply a tenant explicitly with "
            + "SendAsync(command, EdictTenantId.Of(...)) for a public-to-tenant establishing crossing or a "
            + "context-free origin, or fix the resolver.")
    {
    }
}
