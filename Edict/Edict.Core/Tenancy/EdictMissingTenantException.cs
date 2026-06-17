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

    EdictMissingTenantException(string message) : base(message)
    {
    }

    /// <summary>
    /// A tenant-scoped projection read with no ambient tenant. Reading without a tenant
    /// would scope the query to a default partition, so the read is refused at the edge
    /// rather than answered under the wrong wall.
    /// </summary>
    public static EdictMissingTenantException ForProjectionRead(Type projectionType) =>
        new($"Tenancy is on but no tenant resolved for a read of '{projectionType.Name}'. "
            + "The edge resolver registered via AddEdictTenant returned null at the read edge, so the read "
            + "would scope to a default partition rather than the caller's own. Fix the resolver, or read a "
            + "non-tenant projection through IEdictListProjectionReader instead.");

    /// <summary>
    /// A tenant-scoped audit read with no ambient tenant. Reading without a tenant would
    /// surface another wall's trail, so the read is refused at the edge rather than
    /// answered under the wrong tenant.
    /// </summary>
    public static EdictMissingTenantException ForAuditRead() =>
        new("No tenant resolved for a tenant-scoped audit read. The edge resolver registered via "
            + "AddEdictTenant returned null at the read edge, so the read would surface another tenant's "
            + "trail rather than the caller's own. Fix the resolver, or read the whole audit log through "
            + "the operator-scoped IEdictAuditRepository instead.");
}
