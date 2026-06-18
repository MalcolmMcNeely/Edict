using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;

namespace Edict.Core.Tests.Audit;

// A minimal operator-scoped repository double whose query paths echo a fixed record
// set, so the ambient-scoped reader can be tested for the tenant filtering it layers
// on top without standing up a real store. The store, not the reader, would scope by
// correlation/principal/entity in production; here the fixed set stands in for that
// result and the reader is asserted on the tenant filter alone.
sealed class StubAuditRepository(IReadOnlyList<EdictAuditRecord> records) : IEdictAuditRepository
{
    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, EdictTenantId? tenant = null, CancellationToken cancellationToken = default) =>
        Echo(tenant);

    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, DateTimeOffset from, DateTimeOffset to, EdictTenantId? tenant = null, CancellationToken cancellationToken = default) =>
        Echo(tenant);

    public Task<IReadOnlyList<EdictAuditRecord>> ByCorrelationAsync(Guid correlationId, EdictTenantId? tenant = null, CancellationToken cancellationToken = default) =>
        Echo(tenant);

    public Task<IReadOnlyList<EdictAuditRecord>> ByPrincipalAsync(EdictPrincipal principal, DateTimeOffset from, DateTimeOffset to, EdictTenantId? tenant = null, CancellationToken cancellationToken = default) =>
        Echo(tenant);

    // The tenant the most recent query pushed down, so a test can prove the facade
    // scopes in the store rather than re-filtering in memory.
    public EdictTenantId? LastTenantFilter { get; private set; }

    Task<IReadOnlyList<EdictAuditRecord>> Echo(EdictTenantId? tenant)
    {
        LastTenantFilter = tenant;
        IReadOnlyList<EdictAuditRecord> scoped = tenant is null
            ? records
            : records.Where(record => record.Tenant == tenant).ToList();
        return Task.FromResult(scoped);
    }

    public Task<EdictAuditChainVerification> VerifyEntityChainAsync(string entityType, string entityKey, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ReadOnlyMemory<byte>> GetPayloadAsync(Guid recordId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<object> GetMessageAsync(EdictAuditRecord record, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
