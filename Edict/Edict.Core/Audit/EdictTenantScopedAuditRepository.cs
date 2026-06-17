using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;
using Edict.Core.Tenancy;

namespace Edict.Core.Audit;

// Facade over the operator-scoped IEdictAuditRepository that filters every read to the
// caller's ambient tenant, resolved from the same edge resolver the send path uses. The
// operator repository does the correlation/principal/entity query; this drops every
// record whose Tenant is not the ambient one, so a tenant sees only its own trail and
// never another wall's or the public records. A missing ambient tenant fails closed
// rather than returning the unfiltered superset.
sealed class EdictTenantScopedAuditRepository(IEdictAuditRepository inner, IEdictTenantResolver? tenantResolver)
    : IEdictTenantScopedAuditRepository
{
    public async Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(
        string entityType, string entityKey, CancellationToken cancellationToken = default)
    {
        var tenant = AmbientTenant();
        var records = await inner.ByEntityAsync(entityType, entityKey, cancellationToken).ConfigureAwait(false);
        return ScopeToTenant(records, tenant);
    }

    public async Task<IReadOnlyList<EdictAuditRecord>> ByCorrelationAsync(
        Guid correlationId, CancellationToken cancellationToken = default)
    {
        var tenant = AmbientTenant();
        var records = await inner.ByCorrelationAsync(correlationId, cancellationToken).ConfigureAwait(false);
        return ScopeToTenant(records, tenant);
    }

    public async Task<IReadOnlyList<EdictAuditRecord>> ByPrincipalAsync(
        EdictPrincipal principal, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var tenant = AmbientTenant();
        var records = await inner.ByPrincipalAsync(principal, from, to, cancellationToken).ConfigureAwait(false);
        return ScopeToTenant(records, tenant);
    }

    EdictTenantId AmbientTenant() =>
        tenantResolver?.Resolve() ?? throw EdictMissingTenantException.ForAuditRead();

    static IReadOnlyList<EdictAuditRecord> ScopeToTenant(IReadOnlyList<EdictAuditRecord> records, EdictTenantId tenant) =>
        records.Where(record => record.Tenant == tenant).ToList();
}
