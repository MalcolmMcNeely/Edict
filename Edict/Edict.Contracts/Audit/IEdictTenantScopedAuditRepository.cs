using Edict.Contracts.Tenancy;

namespace Edict.Contracts.Audit;

/// <summary>
/// The tenant-scoped read surface over the audit log, scoped to the caller's ambient
/// tenant by construction. Where <see cref="IEdictAuditRepository"/> is the operator
/// superset that returns every record across every tenant (with the public,
/// tenant-less records among them), this filters each query to the resolved ambient
/// tenant, so a business sees only its own trail and neither another tenant's records
/// nor the public ones. Unlike the operator-only dead-letter surface, the audit trail
/// is legitimately the tenant's own to read.
/// <para>
/// A read with no ambient tenant fails closed rather than answering under the wrong
/// wall. Within-tenant authorization (which record this user may see) stays the
/// consumer's job, exactly as with the tenant-scoped projection read.
/// </para>
/// </summary>
public interface IEdictTenantScopedAuditRepository
{
    /// <summary>
    /// One aggregate's records, restricted to the caller's tenant: empty when the
    /// aggregate belongs to another wall. Ordered by chain
    /// <see cref="EdictAuditRecord.Sequence"/>.
    /// </summary>
    Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The caller's own slice of a conversation: every record the conversation touched
    /// that belongs to the ambient tenant, ordered by intent-time. A conversation that
    /// crossed walls returns only this tenant's part.
    /// </summary>
    Task<IReadOnlyList<EdictAuditRecord>> ByConversationAsync(Guid conversationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A principal's activity within the caller's tenant over <c>[from, to)</c>
    /// (<paramref name="from"/> inclusive, <paramref name="to"/> exclusive), ordered by
    /// intent-time. Records the same principal made under another wall are not surfaced.
    /// </summary>
    Task<IReadOnlyList<EdictAuditRecord>> ByPrincipalAsync(EdictPrincipal principal, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
