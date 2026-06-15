using Edict.Contracts.Audit;
using Edict.Core.Audit;
using Edict.Postgres.Audit;

using Npgsql;

using Xunit;

namespace Edict.Postgres.Tests;

// Postgres persistence-axis fixture with auditing on, plus the store-direct read
// and raw-mutation seams the audit conformance asserts against. Bespoke to this
// project rather than a shared battery scenario: the audit store is Postgres-first
// and the Azure provider lands in a later slice, so promoting it to the shared
// persistence battery now would force an Azure binding that does not exist yet.
public sealed class PostgresAuditFixture : PostgresPersistenceFixtureBase
{
    protected override bool EnableAudit => true;

    IEdictAuditStore Store => new PostgresAuditStore(DataSource, ClientSerializer, "edict_audit_record");

    IEdictAuditPayloadStore PayloadStore => new PostgresAuditPayloadStore(DataSource, "edict_audit_payload");

    IEdictAuditRepository Repository => new EdictDefaultAuditRepository(Store, PayloadStore);

    public Task<IReadOnlyList<EdictAuditRecord>> ReadEntityAsync(string entityType, string entityKey) =>
        Store.ByEntityAsync(entityType, entityKey, CancellationToken.None);

    public Task<ReadOnlyMemory<byte>> GetPayloadAsync(Guid recordId) =>
        PayloadStore.GetAsync(recordId, CancellationToken.None);

    public Task<EdictAuditChainVerification> VerifyChainAsync(string entityType, string entityKey) =>
        Repository.VerifyEntityChainAsync(entityType, entityKey);

    public Task<IReadOnlyList<EdictAuditRecord>> ByCorrelationAsync(Guid correlationId) =>
        Repository.ByCorrelationAsync(correlationId);

    public Task<IReadOnlyList<EdictAuditRecord>> ByPrincipalAsync(EdictPrincipal principal, DateTimeOffset from, DateTimeOffset to) =>
        Repository.ByPrincipalAsync(principal, from, to);

    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityInRangeAsync(string entityType, string entityKey, DateTimeOffset from, DateTimeOffset to) =>
        Repository.ByEntityAsync(entityType, entityKey, from, to);

    // Runs a raw mutation and returns the SQLSTATE Postgres rejected it with, or
    // null when it (wrongly) succeeded. The WORM trigger raises insufficient
    // privilege (42501) for an UPDATE or DELETE.
    public async Task<string?> TryRawSqlAsync(string sql)
    {
        await using var connection = await DataSource.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        try
        {
            await command.ExecuteNonQueryAsync();
            return null;
        }
        catch (PostgresException exception)
        {
            return exception.SqlState;
        }
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresAuditCollection : ICollectionFixture<PostgresAuditFixture>
{
    public const string Name = "PostgresAudit";
}
