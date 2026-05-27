using Edict.Contracts.TableStorage;
using Edict.Core.TableStorage;

using Npgsql;

using NpgsqlTypes;

using Orleans.Serialization;

namespace Edict.Postgres.TableStorage;

/// <summary>
/// Postgres implementation of <see cref="IEdictTableStoreFactory"/>. Creates
/// a generic projection table on demand (idempotent <c>CREATE TABLE IF NOT
/// EXISTS</c>) and hands back a <see cref="PostgresTableWriteStore{T}"/>
/// bound to it. The factory also services the framework-internal one-shot
/// <see cref="UpsertRowAsync"/> overload used by the Outbox's UpsertRow
/// effect executor.
/// </summary>
public sealed class PostgresTableWriteStoreFactory : IEdictTableStoreFactory
{
    readonly string _connectionString;
    readonly Serializer _serializer;

    public PostgresTableWriteStoreFactory(string connectionString, Serializer serializer)
    {
        _connectionString = connectionString;
        _serializer = serializer;
    }

    public async Task<IEdictTableWriteStore<T>> CreateAsync<T>(string tableName, CancellationToken cancellationToken = default)
        where T : class, new()
    {
        await PostgresTableSchema.EnsureProjectionTableAsync(_connectionString, tableName, cancellationToken);
        return new PostgresTableWriteStore<T>(_connectionString, tableName, _serializer);
    }

    public async Task UpsertRowAsync(
        string tableName,
        string partitionKey,
        string rowKey,
        object row,
        CancellationToken cancellationToken = default)
    {
        await PostgresTableSchema.EnsureProjectionTableAsync(_connectionString, tableName, cancellationToken);

        var quoted = PostgresTableSchema.QuoteIdentifier(tableName);
        // The row arrives as an object (the Outbox UpsertRow executor
        // deserialises from JSON to the concrete type), so dispatch
        // SerializeToArray<T> at the runtime type. Reflection here is fine —
        // each effect drain is a per-grain-turn ceremony, not a hot loop.
        var bytes = SerializeToArrayBoxed(_serializer, row);
        var etag = Guid.NewGuid().ToString("N");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
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

    static byte[] SerializeToArrayBoxed(Serializer serializer, object row)
    {
        var method = typeof(Serializer).GetMethods()
            .FirstOrDefault(m => m.Name == nameof(Serializer.SerializeToArray)
                && m.IsGenericMethodDefinition
                && m.GetParameters().Length == 1)
            ?? throw new InvalidOperationException(
                "Orleans Serializer.SerializeToArray<T>(T) not found.");
        var bytes = method.MakeGenericMethod(row.GetType()).Invoke(serializer, [row])
            ?? throw new InvalidOperationException(
                $"SerializeToArray returned null for {row.GetType().FullName}.");
        return (byte[])bytes;
    }
}
