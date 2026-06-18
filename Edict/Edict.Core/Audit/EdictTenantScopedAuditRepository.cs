using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;
using Edict.Core.Tenancy;

namespace Edict.Core.Audit;

// Facade over the operator-scoped IEdictAuditRepository that scopes every read to the
// caller's ambient tenant, resolved from the same edge resolver the send path uses. The
// resolved wall is pushed down as the operator query's tenant filter, so the predicate
// runs in the store and another wall's rows never leave it; the in-memory ScopeToTenant
// stays only as cheap belt-and-braces over a store that ignored the predicate. A missing
// ambient tenant fails closed rather than returning the unfiltered superset.
sealed class EdictTenantScopedAuditRepository(IEdictAuditRepository inner, IEdictTenantResolver? tenantResolver)
    : IEdictTenantScopedAuditRepository
{
    public async Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(
        string entityType, string entityKey, CancellationToken cancellationToken = default)
    {
        var tenant = AmbientTenant();
        var records = await inner.ByEntityAsync(entityType, entityKey, tenant, cancellationToken).ConfigureAwait(false);
        return ScopeToTenant(records, tenant);
    }

    public async Task<IReadOnlyList<EdictAuditRecord>> ByCorrelationAsync(
        Guid correlationId, CancellationToken cancellationToken = default)
    {
        var tenant = AmbientTenant();
        var records = await inner.ByCorrelationAsync(correlationId, tenant, cancellationToken).ConfigureAwait(false);
        return ScopeToTenant(records, tenant);
    }

    public async Task<IReadOnlyList<EdictAuditRecord>> ByPrincipalAsync(
        EdictPrincipal principal, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var tenant = AmbientTenant();
        var records = await inner.ByPrincipalAsync(principal, from, to, tenant, cancellationToken).ConfigureAwait(false);
        return ScopeToTenant(records, tenant);
    }

    EdictTenantId AmbientTenant() =>
        tenantResolver?.Resolve() ?? throw EdictMissingTenantException.ForAuditRead();

    static IReadOnlyList<EdictAuditRecord> ScopeToTenant(IReadOnlyList<EdictAuditRecord> records, EdictTenantId tenant) =>
        records.Where(record => record.Tenant == tenant).ToList();
}
