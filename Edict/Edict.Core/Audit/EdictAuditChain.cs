using Edict.Contracts.Audit;

namespace Edict.Core.Audit;

/// <summary>
/// Verifies an audit chain a consumer already holds in memory, without touching
/// the store. <see cref="IEdictAuditRepository.VerifyEntityChainAsync"/> re-walks
/// the records the store returns; this verifies a list passed in directly, so a
/// consumer can prove a deliberately altered copy is reported broken even when
/// the backing store is write-once and refuses the tamper outright.
/// </summary>
public static class EdictAuditChain
{
    /// <summary>
    /// Recomputes every record's hash from its own stored content and checks each
    /// links to its predecessor, returning the first record whose hash or linkage
    /// fails. The records must be in chain (<see cref="EdictAuditRecord.Sequence"/>)
    /// order, as the repository query paths return them.
    /// </summary>
    public static EdictAuditChainVerification Verify(IReadOnlyList<EdictAuditRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        return HashChain.Verify(records);
    }
}
