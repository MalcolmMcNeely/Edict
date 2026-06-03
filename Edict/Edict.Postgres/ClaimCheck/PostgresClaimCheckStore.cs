using Edict.Contracts.ClaimCheck;
using Edict.Core.DeadLetter;

using Npgsql;

using NpgsqlTypes;

namespace Edict.Postgres.ClaimCheck;

sealed class PostgresClaimCheckStore : IEdictClaimCheckStore
{
    readonly NpgsqlDataSource _dataSource;
    readonly string _tableName;

    public PostgresClaimCheckStore(NpgsqlDataSource dataSource, string tableName)
    {
        _dataSource = dataSource;
        _tableName = tableName;
    }

    public async Task PutAsync(Guid eventId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"INSERT INTO {_tableName} (id, payload, created_at) VALUES (@id, @payload, now());";
            command.Parameters.AddWithValue("id", eventId);
            command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Bytea)
            {
                Value = payload.ToArray(),
            });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw EdictPostgresStorageException.From(exception,
                $"PutAsync failed for claim-check table {_tableName}");
        }
    }

    public async Task<ReadOnlyMemory<byte>> GetAsync(Guid eventId, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT payload FROM {_tableName} WHERE id = @id;";
            command.Parameters.AddWithValue("id", eventId);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is null || result is DBNull)
            {
                throw new EdictClaimCheckFetchException(
                    eventId,
                    $"Claim-check payload not found for event '{eventId:N}'.");
            }
            return (byte[])result;
        }
        catch (NpgsqlException exception)
        {
            throw EdictPostgresStorageException.From(exception,
                $"GetAsync failed for claim-check table {_tableName} (event {eventId:N})");
        }
    }
}
