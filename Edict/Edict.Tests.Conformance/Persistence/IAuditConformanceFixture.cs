using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;

namespace Edict.Tests.Conformance.Persistence;

/// <summary>
/// The audit-read seam a persistence fixture adds on top of
/// <see cref="PersistenceConformanceFixture"/> so the shared audit scenarios can
/// assert capture against a real backing store without learning its SDK. A fixture
/// implementing this stands up a silo with audit capture on (<c>WithAudit()</c>)
/// under a known <see cref="AuditPrincipal"/>, and exposes the read paths
/// store-direct so a scenario verifies what landed independent of the grain read
/// path. The reads return only public contract types, keeping the conformance
/// assembly free of any provider storage SDK.
/// </summary>
public interface IAuditConformanceFixture
{
    /// <summary>The known principal this fixture's origin resolver stamps, so a scenario can assert attribution.</summary>
    EdictPrincipal AuditPrincipal { get; }

    Task<IReadOnlyList<EdictAuditRecord>> ReadEntityAsync(string entityType, string entityKey);

    Task<ReadOnlyMemory<byte>> GetPayloadAsync(Guid recordId);

    Task<EdictAuditChainVerification> VerifyChainAsync(string entityType, string entityKey);

    Task<IReadOnlyList<EdictAuditRecord>> ByCorrelationAsync(Guid correlationId);

    Task<IReadOnlyList<EdictAuditRecord>> ByPrincipalAsync(EdictPrincipal principal, DateTimeOffset from, DateTimeOffset to);

    Task<IReadOnlyList<EdictAuditRecord>> ByEntityInRangeAsync(string entityType, string entityKey, DateTimeOffset from, DateTimeOffset to);

    /// <summary>
    /// Writes audit records straight to the backing store, so a tenant-scope scenario
    /// can seed records under several walls without standing up tenancy wiring on the
    /// capture silo (the conformance silo captures public, tenant-less records).
    /// </summary>
    Task AppendAsync(IReadOnlyList<EdictAuditRecord> records);

    /// <summary>
    /// The operator read with a tenant filter: a non-null <paramref name="tenant"/>
    /// narrows to one wall via the store predicate, a null filter returns the operator
    /// superset across every wall. Proves the predicate runs in the database.
    /// </summary>
    Task<IReadOnlyList<EdictAuditRecord>> OperatorByCorrelationAsync(Guid correlationId, EdictTenantId? tenant);

    /// <summary>
    /// The ambient-scoped read over the real store, resolving the given ambient tenant:
    /// returns only that wall's rows (predicate pushed into the store) and fails closed
    /// when <paramref name="ambientTenant"/> is <see langword="null"/>.
    /// </summary>
    Task<IReadOnlyList<EdictAuditRecord>> TenantScopedByCorrelationAsync(Guid correlationId, EdictTenantId? ambientTenant);
}
