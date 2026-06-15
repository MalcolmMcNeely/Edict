using Edict.Contracts.Audit;

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
                    + "(record_id, entity_type, entity_key, sequence, correlation_id, principal, kind, outcome, occurred_at, record) "
                    + "VALUES (@record_id, @entity_type, @entity_key, @sequence, @correlation_id, @principal, @kind, @outcome, @occurred_at, @record) "
                    + "ON CONFLICT (record_id) DO NOTHING;");
                command.Parameters.AddWithValue("record_id", record.RecordId);
                command.Parameters.AddWithValue("entity_type", record.EntityType);
                command.Parameters.AddWithValue("entity_key", record.EntityKey);
                command.Parameters.AddWithValue("sequence", record.Sequence);
                command.Parameters.AddWithValue("correlation_id", record.CorrelationId);
                command.Parameters.AddWithValue("principal", (object?)record.Principal?.Value ?? DBNull.Value);
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

    public async Task<IReadOnlyList<EdictAuditRecord>> ByEntityAsync(string entityType, string entityKey, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT record FROM {tableName} WHERE entity_type = @entity_type AND entity_key = @entity_key ORDER BY sequence;";
            command.Parameters.AddWithValue("entity_type", entityType);
            command.Parameters.AddWithValue("entity_key", entityKey);

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
            throw EdictPostgresStorageException.From(exception, $"ByEntityAsync failed for audit table {tableName}");
        }
    }
}
