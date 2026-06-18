using Edict.Contracts.Audit;
using Edict.Contracts.Tenancy;

using Npgsql;

using NpgsqlTypes;

using Orleans.Serialization;

namespace Edict.Postgres.Audit;

// Postgres-backed audit chain store. The full record (chain hashes included)
// round-trips through the contract serializer in the `record` column; the
// structured columns exist only to query and order. Append is idempotent
// (ON CONFLICT DO NOTHING) so a re-drain after a crash between the store write
// and the grain's ack is a no-op. The table is WORM by trigger, so this store
// never offers an update or delete.
sealed class PostgresAuditStore(NpgsqlDataSource dataSource, Serializer serializer, string tableName) : IEdictAuditStore
{
    public async Task AppendAsync(IReadOnlyList<EdictAuditRecord> records, CancellationToken cancellationToken)
    {
        if (records.Count == 0)
        {
            return;
        }

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var batch = new NpgsqlBatch(connection);
            foreach (var record in records)
            {
                var command = new NpgsqlBatchCommand(
                    $"INSERT INTO {tableName} "
                    + "(record_id, entity_type, entity_key, sequence, correlation_id, principal, tenant, kind, outcome, occurred_at, record) "
                    + "VALUES (@record_id, @entity_type, @entity_key, @sequence, @correlation_id, @principal, @tenant, @kind, @outcome, @occurred_at, @record) "
                    + "ON CONFLICT (record_id) DO NOTHING;");
                command.Parameters.AddWithValue("record_id", record.RecordId);
                command.Parameters.AddWithValue("entity_type", record.EntityType);
                command.Parameters.AddWithValue("entity_key", record.EntityKey);
                command.Parameters.AddWithValue("sequence", record.Sequence);
                command.Parameters.AddWithValue("correlation_id", record.CorrelationId);
                command.Parameters.AddWithValue("principal", (object?)record.Principal?.Value ?? DBNull.Value);
                command.Parameters.AddWithValue("tenant", (object?)record.Tenant?.Value ?? DBNull.Value);
                command.Parameters.AddWithValue("kind", record.Kind.ToString());
                command.Parameters.AddWithValue("outcome", (object?)record.Outcome?.ToString() ?? DBNull.Value);
                command.Parameters.AddWithValue("occurred_at", record.OccurredAt);
                command.Parameters.Add(new NpgsqlParameter("record", NpgsqlDbType.Bytea)
                {
                    Value = serializer.SerializeToArray(record),
                });
                batch.BatchCommands.Add(command);
            }

            await batch.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw EdictPostgresStorageException.From(exception, $"AppendAsync failed for audit table {tableName}");
        }
    }

    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, EdictTenantId? tenant, CancellationToken cancellationToken) =>
        QueryAsync(
            $"SELECT record FROM {tableName} WHERE entity_type = @entity_type AND entity_key = @entity_key"
            + TenantPredicate(tenant) + " ORDER BY sequence;",
            parameters =>
            {
                parameters.AddWithValue("entity_type", entityType);
                parameters.AddWithValue("entity_key", entityKey);
                BindTenant(parameters, tenant);
            },
            nameof(ByEntityAsync),
            cancellationToken);

    public Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, DateTimeOffset from, DateTimeOffset to, EdictTenantId? tenant, CancellationToken cancellationToken) =>
        QueryAsync(
            $"SELECT record FROM {tableName} WHERE entity_type = @entity_type AND entity_key = @entity_key "
            + "AND occurred_at >= @from AND occurred_at < @to" + TenantPredicate(tenant) + " ORDER BY sequence;",
            parameters =>
            {
                parameters.AddWithValue("entity_type", entityType);
                parameters.AddWithValue("entity_key", entityKey);
                parameters.AddWithValue("from", from);
                parameters.AddWithValue("to", to);
                BindTenant(parameters, tenant);
            },
            nameof(ByEntityAsync),
            cancellationToken);

    // The chain is per-aggregate, so a correlation that fanned across grains has no
    // single sequence to follow; intent-time rebuilds the cross-grain order, with
    // entity and sequence as a stable tie-break when two records share a timestamp.
    public Task<IReadOnlyList<EdictAuditRecord>> ByCorrelationAsync(Guid correlationId, EdictTenantId? tenant, CancellationToken cancellationToken) =>
        QueryAsync(
            $"SELECT record FROM {tableName} WHERE correlation_id = @correlation_id" + TenantPredicate(tenant)
            + " ORDER BY occurred_at, entity_type, entity_key, sequence;",
            parameters =>
            {
                parameters.AddWithValue("correlation_id", correlationId);
                BindTenant(parameters, tenant);
            },
            nameof(ByCorrelationAsync),
            cancellationToken);

    public Task<IReadOnlyList<EdictAuditRecord>> ByPrincipalAsync(EdictPrincipal principal, DateTimeOffset from, DateTimeOffset to, EdictTenantId? tenant, CancellationToken cancellationToken) =>
        QueryAsync(
            $"SELECT record FROM {tableName} WHERE principal = @principal "
            + "AND occurred_at >= @from AND occurred_at < @to" + TenantPredicate(tenant)
            + " ORDER BY occurred_at, entity_type, entity_key, sequence;",
            parameters =>
            {
                parameters.AddWithValue("principal", principal.Value);
                parameters.AddWithValue("from", from);
                parameters.AddWithValue("to", to);
                BindTenant(parameters, tenant);
            },
            nameof(ByPrincipalAsync),
            cancellationToken);

    // A null filter is the operator superset; a non-null filter pushes the wall into
    // the SQL so another tenant's rows never leave the database.
    static string TenantPredicate(EdictTenantId? tenant) =>
        tenant is null ? string.Empty : " AND tenant = @tenant";

    static void BindTenant(NpgsqlParameterCollection parameters, EdictTenantId? tenant)
    {
        if (tenant is { } value)
        {
            parameters.AddWithValue("tenant", value.Value);
        }
    }

    async Task<IReadOnlyList<EdictAuditRecord>> QueryAsync(
        string commandText, Action<NpgsqlParameterCollection> bindParameters, string operation, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = commandText;
            bindParameters(command.Parameters);

            var results = new List<EdictAuditRecord>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(serializer.Deserialize<EdictAuditRecord>((byte[])reader[0]));
            }
            return results;
        }
        catch (NpgsqlException exception)
        {
            throw EdictPostgresStorageException.From(exception, $"{operation} failed for audit table {tableName}");
        }
    }
}
