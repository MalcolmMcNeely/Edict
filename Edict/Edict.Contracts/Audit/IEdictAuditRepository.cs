namespace Edict.Contracts.Audit;

/// <summary>
/// The consumer-facing read surface over the audit log. This slice exposes one
/// aggregate's history (<see cref="ByEntityAsync"/>), a first-class chain
/// verification (<see cref="VerifyEntityChainAsync"/>), and retrieval of a
/// captured body (<see cref="GetPayloadAsync"/>); the cross-cutting query paths
/// (by correlation, by principal) follow as later slices.
/// </summary>
public interface IEdictAuditRepository
{
    /// <summary>Every audit record for one aggregate, ordered by chain <see cref="EdictAuditRecord.Sequence"/>.</summary>
    Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies one aggregate's chain is unaltered: every record's hash
    /// recomputes from its stored content and links to its predecessor. Reports
    /// the first broken record so an auditor knows where the history diverged.
    /// </summary>
    Task<EdictAuditChainVerification> VerifyEntityChainAsync(string entityType, string entityKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The captured body behind a record's <see cref="EdictAuditRecord.PayloadReference"/>:
    /// the serialized message bytes the record's <see cref="EdictAuditRecord.PayloadHash"/>
    /// was computed over, so an auditor sees <em>what</em> was decided, not merely
    /// that a decision happened. Retrieved by record id from the separate,
    /// separately-addressable payload store.
    /// </summary>
    Task<ReadOnlyMemory<byte>> GetPayloadAsync(Guid recordId, CancellationToken cancellationToken = default);
}
