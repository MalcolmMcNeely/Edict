using Edict.Contracts.TableStorage;

using Npgsql;

using NpgsqlTypes;

using Orleans.Serialization;

namespace Edict.Postgres.TableStorage;

/// <summary>
/// Postgres write-store backing <see cref="IEdictTableWriteStore{T}"/>. The
/// row is serialised via the silo's <see cref="Serializer"/> into the
/// <c>payload</c> bytea column; <c>etag</c> is regenerated on every upsert
/// for symmetry with Azure Tables' optimistic-concurrency story (Edict's
/// table projections don't currently exercise the etag — the column is
/// reserved for the next iteration).
/// </summary>
internal sealed class PostgresTableWriteStore<T> : IEdictTableWriteStore<T>
    where T : class, new()
{
    readonly NpgsqlDataSource _dataSource;
    readonly string _tableName;
    readonly Serializer _serializer;

    internal PostgresTableWriteStore(NpgsqlDataSource dataSource, string tableName, Serializer serializer)
    {
        _dataSource = dataSource;
        _tableName = tableName;
        _serializer = serializer;
    }

    public async Task<T?> GetAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default)
    {
        var quoted = PostgresTableSchema.QuoteIdentifier(_tableName);
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT payload FROM {quoted} WHERE partition_key = @pk AND row_key = @rk;";
            command.Parameters.AddWithValue("pk", partitionKey);
            command.Parameters.AddWithValue("rk", rowKey);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is null || result is DBNull)
            {
                return null;
            }
            return _serializer.Deserialize<T>((byte[])result);
        }
        catch (NpgsqlException exception)
        {
            throw EdictPostgresStorageException.From(exception,
                $"GetAsync failed for {_tableName} ({partitionKey}/{rowKey})");
        }
    }

    public async Task UpsertAsync(string partitionKey, string rowKey, T row, CancellationToken cancellationToken = default)
    {
        var quoted = PostgresTableSchema.QuoteIdentifier(_tableName);
        var bytes = _serializer.SerializeToArray(row);
        var etag = Guid.NewGuid().ToString("N");

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"INSERT INTO {quoted} (partition_key, row_key, payload, etag) " +
                "VALUES (@pk, @rk, @payload, @etag) " +
                "ON CONFLICT (partition_key, row_key) DO UPDATE SET " +
                "payload = EXCLUDED.payload, etag = EXCLUDED.etag;";
            command.Parameters.AddWithValue("pk", partitionKey);
            command.Parameters.AddWithValue("rk", rowKey);
            command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Bytea) { Value = bytes });
            command.Parameters.AddWithValue("etag", etag);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw EdictPostgresStorageException.From(exception,
                $"UpsertAsync failed for {_tableName} ({partitionKey}/{rowKey})");
        }
    }
}
