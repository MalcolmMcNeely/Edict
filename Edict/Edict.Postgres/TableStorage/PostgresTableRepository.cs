using Edict.Contracts.TableStorage;

using Npgsql;

using Orleans.Serialization;

namespace Edict.Postgres.TableStorage;

/// <summary>
/// Postgres implementation of <see cref="IEdictTableRepository{T}"/>. Reads the
/// MessagePack-/Orleans-Serializer-encoded payload column out of the
/// per-projection table and round-trips it through the silo's
/// <see cref="Serializer"/>, so projection-row types declared with
/// <c>[GenerateSerializer]</c> (consumer projections) or
/// <c>[MessagePackObject]</c> (framework contracts like
/// <see cref="Edict.Contracts.DeadLetter.EdictDeadLetterEntry"/>) round-trip
/// without a separate POCO mapper.
/// </summary>
public sealed class PostgresTableRepository<T> : IEdictTableRepository<T>
    where T : class, new()
{
    readonly string _connectionString;
    readonly string _tableName;
    readonly Serializer _serializer;

    public PostgresTableRepository(string connectionString, string tableName, Serializer serializer)
    {
        _connectionString = connectionString;
        _tableName = tableName;
        _serializer = serializer;
    }

    public async Task<T?> GetAsync(string partitionKey, string rowKey, CancellationToken cancellationToken = default)
    {
        var quoted = PostgresTableSchema.QuoteIdentifier(_tableName);
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT payload FROM {quoted} WHERE partition_key = @pk AND row_key = @rk;";
        command.Parameters.AddWithValue("pk", partitionKey);
        command.Parameters.AddWithValue("rk", rowKey);
        try
        {
            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is null || result is DBNull)
            {
                return null;
            }
            return _serializer.Deserialize<T>((byte[])result);
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // Table does not exist yet — projection hasn't run, return null.
            return null;
        }
    }

    public async Task<IReadOnlyList<T>> QueryPartitionAsync(string partitionKey, CancellationToken cancellationToken = default)
    {
        var quoted = PostgresTableSchema.QuoteIdentifier(_tableName);
        var results = new List<T>();
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT payload FROM {quoted} WHERE partition_key = @pk;";
        command.Parameters.AddWithValue("pk", partitionKey);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var bytes = (byte[])reader["payload"];
                results.Add(_serializer.Deserialize<T>(bytes));
            }
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // Table does not exist yet — return empty.
        }
        return results;
    }
}
