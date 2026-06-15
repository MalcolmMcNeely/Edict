using Edict.Contracts.Audit;
using Edict.Core.Audit;

using Npgsql;

using NpgsqlTypes;

namespace Edict.Postgres.Audit;

// Postgres-backed audit payload store: the captured message bodies, keyed by audit
// record id. Put is idempotent (ON CONFLICT DO NOTHING) so a re-drain after a crash
// between the body write and the grain's ack is a no-op. The table is WORM by
// trigger, so this store never offers an update or delete. Get throws
// EdictAuditPayloadNotFoundException for an id the store does not hold.
sealed class PostgresAuditPayloadStore(NpgsqlDataSource dataSource, string tableName) : IEdictAuditPayloadStore
{
    public async Task PutAsync(IReadOnlyList<EdictAuditPayload> payloads, CancellationToken cancellationToken)
    {
        if (payloads.Count == 0)
        {
            return;
        }

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var batch = new NpgsqlBatch(connection);
            foreach (var payload in payloads)
            {
                var command = new NpgsqlBatchCommand(
                    $"INSERT INTO {tableName} (record_id, payload) VALUES (@record_id, @payload) "
                    + "ON CONFLICT (record_id) DO NOTHING;");
                command.Parameters.AddWithValue("record_id", payload.RecordId);
                command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Bytea)
                {
                    Value = payload.Body.ToArray(),
                });
                batch.BatchCommands.Add(command);
            }

            await batch.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw EdictPostgresStorageException.From(exception, $"PutAsync failed for audit payload table {tableName}");
        }
    }

    public async Task<ReadOnlyMemory<byte>> GetAsync(Guid recordId, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT payload FROM {tableName} WHERE record_id = @record_id;";
            command.Parameters.AddWithValue("record_id", recordId);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is null or DBNull)
            {
                throw new EdictAuditPayloadNotFoundException(
                    recordId, $"No audit payload for record '{recordId:N}' in {tableName}.");
            }
            return (byte[])result;
        }
        catch (NpgsqlException exception)
        {
            throw EdictPostgresStorageException.From(exception, $"GetAsync failed for audit payload table {tableName} (record {recordId:N})");
        }
    }
}
