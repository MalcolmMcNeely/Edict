using Azure.Data.Tables;

using Edict.Azure.Persistence.Audit;
using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;
using Edict.Core.Audit;
using Edict.Core.Tenancy;
using Edict.Tests.Conformance.Persistence;

using Xunit;

namespace Edict.Azure.Persistence.Tests;

// Azure persistence-axis fixture with auditing on, plus the store-direct read seam
// the audit conformance asserts against. Implements the shared
// IAuditConformanceFixture so the promoted battery scenarios bind on Azure. The
// Azure-Table chain is tamper-evidence without infra prevention, so this fixture also
// exposes a raw-mutation seam for the Azure-bespoke tamper-evidence test — the
// analogue of the Postgres WORM tests, except here the mutation succeeds and the
// chain verification is what surfaces it.
public sealed class AzureAuditFixture : AzurePersistenceFixtureBase, IAuditConformanceFixture
{
    protected override bool EnableAudit => true;

    public EdictPrincipal AuditPrincipal => KnownAuditPrincipal;

    IEdictAuditStore Store => AzureTableAuditStore.Create(TableServiceClient, ClientSerializer, AuditTableName);

    IEdictAuditPayloadStore PayloadStore => AzureBlobAuditPayloadStore.Create(BlobServiceClient, AuditPayloadContainerName);

    IEdictAuditRepository Repository => new EdictDefaultAuditRepository(Store, PayloadStore, ClientSerializer);

    public Task<IReadOnlyList<EdictAuditRecord>> ReadEntityAsync(string entityType, string entityKey) =>
        Store.ByEntityAsync(entityType, entityKey, tenant: null, CancellationToken.None);

    public Task<ReadOnlyMemory<byte>> GetPayloadAsync(Guid recordId) =>
        PayloadStore.GetAsync(recordId, CancellationToken.None);

    public Task<EdictAuditChainVerification> VerifyChainAsync(string entityType, string entityKey) =>
        Repository.VerifyEntityChainAsync(entityType, entityKey);

    public Task<IReadOnlyList<EdictAuditRecord>> ByConversationAsync(Guid correlationId) =>
        Repository.ByConversationAsync(correlationId);

    public Task<IReadOnlyList<EdictAuditRecord>> ByPrincipalAsync(EdictPrincipal principal, DateTimeOffset from, DateTimeOffset to) =>
        Repository.ByPrincipalAsync(principal, from, to);

    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityInRangeAsync(string entityType, string entityKey, DateTimeOffset from, DateTimeOffset to) =>
        Repository.ByEntityAsync(entityType, entityKey, from, to);

    public Task AppendAsync(IReadOnlyList<EdictAuditRecord> records) =>
        Store.AppendAsync(records, CancellationToken.None);

    public Task<IReadOnlyList<EdictAuditRecord>> OperatorByCorrelationAsync(Guid correlationId, EdictTenantId? tenant) =>
        Repository.ByConversationAsync(correlationId, tenant);

    public Task<IReadOnlyList<EdictAuditRecord>> TenantScopedByCorrelationAsync(Guid correlationId, EdictTenantId? ambientTenant) =>
        new EdictTenantScopedAuditRepository(Repository, new FixedTenantResolver(ambientTenant)).ByConversationAsync(correlationId);

    sealed class FixedTenantResolver(EdictTenantId? tenant) : IEdictTenantResolver
    {
        public EdictTenantId? Resolve() => tenant;
    }

    // Rewrites the entity-path row's stored record under a tampered principal, the
    // mutation the Postgres WORM trigger refuses outright. On Azure it succeeds, so
    // the hash chain is the only thing that catches it.
    public async Task TamperEntityRecordPrincipalAsync(string entityType, string entityKey, Guid recordId)
    {
        var tableClient = TableServiceClient.GetTableClient(AuditTableName);
        var partitionKey = AzureTableAuditStore.EntityPartition(entityType, entityKey);
        var rowKey = recordId.ToString("N");

        var response = await tableClient.GetEntityAsync<TableEntity>(partitionKey, rowKey);
        var entity = response.Value;

        var original = ClientSerializer.Deserialize<EdictAuditRecord>(entity.GetBinary(AzureTableAuditStore.RecordColumn));
        var tampered = original with { Principal = EdictPrincipal.Of("tampered") };
        entity[AzureTableAuditStore.RecordColumn] = ClientSerializer.SerializeToArray(tampered);

        await tableClient.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);
    }
}

[CollectionDefinition(Name)]
public sealed class AzureAuditCollection : ICollectionFixture<AzureAuditFixture>
{
    public const string Name = "AzureAudit";
}
