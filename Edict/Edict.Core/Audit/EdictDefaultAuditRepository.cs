using Edict.Contracts.Audit;

namespace Edict.Core.Audit;

// The default consumer read surface over whatever IEdictAuditStore the substrate
// registered. ByEntity reads straight through; chain verification re-walks the
// stored records with the pure HashChain so the tamper-evidence logic is the same
// one the unit tests pin, independent of the backing store.
sealed class EdictDefaultAuditRepository(IEdictAuditStore store) : IEdictAuditRepository
{
    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(
        string entityType, string entityKey, CancellationToken cancellationToken = default) =>
        store.ByEntityAsync(entityType, entityKey, cancellationToken);

    public async Task<EdictAuditChainVerification> VerifyEntityChainAsync(
        string entityType, string entityKey, CancellationToken cancellationToken = default)
    {
        var records = await store.ByEntityAsync(entityType, entityKey, cancellationToken);
        return HashChain.Verify(records);
    }
}
