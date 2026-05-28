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
    readonly NpgsqlDataSource _dataSource;
    readonly string _tableName;
    readonly Serializer _serializer;

    public PostgresTableRepository(NpgsqlDataSource dataSource, string tableName, Serializer serializer)
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
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // Table does not exist yet — projection hasn't run, return null.
            return null;
        }
        catch (NpgsqlException ex)
        {
            throw EdictPostgresStorageException.From(ex,
                $"GetAsync failed for {_tableName} ({partitionKey}/{rowKey})");
        }
    }

    public async Task<IReadOnlyList<T>> QueryPartitionAsync(string partitionKey, CancellationToken cancellationToken = default)
    {
        var quoted = PostgresTableSchema.QuoteIdentifier(_tableName);
        var results = new List<T>();
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT payload FROM {quoted} WHERE partition_key = @pk;";
            command.Parameters.AddWithValue("pk", partitionKey);
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
        catch (NpgsqlException ex)
        {
            throw EdictPostgresStorageException.From(ex,
                $"QueryPartitionAsync failed for {_tableName} ({partitionKey})");
        }
        return results;
    }
}
