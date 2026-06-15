using Edict.Contracts.Audit;

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
}
