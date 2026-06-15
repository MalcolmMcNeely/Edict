namespace Edict.Contracts.Audit;

/// <summary>
/// The consumer-facing read surface over the audit log. This slice exposes one
/// aggregate's history (<see cref="ByEntityAsync"/>) and a first-class chain
/// verification (<see cref="VerifyEntityChainAsync"/>); the cross-cutting
/// query paths (by correlation, by principal) and payload retrieval follow as
/// later slices.
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
}
