using Edict.Contracts.Audit;

using Orleans.Serialization;

namespace Edict.Core.Audit;

// The default consumer read surface over whatever audit stores the substrate
// registered. The query paths (by entity, by correlation, by principal) and
// GetPayload read straight through; chain verification re-walks the stored
// records with the pure HashChain so the tamper-evidence logic is the same one
// the unit tests pin, independent of the backing store. GetMessage fetches the
// body and hands it to the pure AuditMessageDeserializer, which boxes the
// concrete message back.
sealed class EdictDefaultAuditRepository(IEdictAuditStore store, IEdictAuditPayloadStore payloadStore, Serializer serializer) : IEdictAuditRepository
{
    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(
        string entityType, string entityKey, CancellationToken cancellationToken = default) =>
        store.ByEntityAsync(entityType, entityKey, cancellationToken);

    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(
        string entityType, string entityKey, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        store.ByEntityAsync(entityType, entityKey, from, to, cancellationToken);

    public Task<IReadOnlyList<EdictAuditRecord>> ByCorrelationAsync(
        Guid correlationId, CancellationToken cancellationToken = default) =>
        store.ByCorrelationAsync(correlationId, cancellationToken);

    public Task<IReadOnlyList<EdictAuditRecord>> ByPrincipalAsync(
        EdictPrincipal principal, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        store.ByPrincipalAsync(principal, from, to, cancellationToken);

    public async Task<EdictAuditChainVerification> VerifyEntityChainAsync(
        string entityType, string entityKey, CancellationToken cancellationToken = default)
    {
        var records = await store.ByEntityAsync(entityType, entityKey, cancellationToken);
        return HashChain.Verify(records);
    }

    public Task<ReadOnlyMemory<byte>> GetPayloadAsync(Guid recordId, CancellationToken cancellationToken = default) =>
        payloadStore.GetAsync(recordId, cancellationToken);

    public async Task<object> GetMessageAsync(EdictAuditRecord record, CancellationToken cancellationToken = default)
    {
        var body = await payloadStore.GetAsync(record.PayloadReference, cancellationToken);
        return AuditMessageDeserializer.Deserialize(serializer, body.ToArray(), record);
    }
}
