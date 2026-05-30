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

    public async Task<string> PutAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"INSERT INTO {_tableName} (id, payload, created_at) VALUES (@id, @payload, now());";
            command.Parameters.AddWithValue("id", id);
            command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Bytea)
            {
                Value = payload.ToArray(),
            });
            await command.ExecuteNonQueryAsync(cancellationToken);
            return id.ToString("N");
        }
        catch (NpgsqlException exception)
        {
            throw EdictPostgresStorageException.From(exception,
                $"PutAsync failed for claim-check table {_tableName}");
        }
    }

    public async Task<ReadOnlyMemory<byte>> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(key, "N", out var id))
        {
            throw new EdictClaimCheckFetchException(
                EdictClaimCheckFetchException.Reason.KeyMalformed,
                key,
                $"Claim-check key '{key}' is not in the expected GUID-N format.");
        }

        try
        {
            await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT payload FROM {_tableName} WHERE id = @id;";
            command.Parameters.AddWithValue("id", id);
            var result = await command.ExecuteScalarAsync(cancellationToken);
            if (result is null || result is DBNull)
            {
                throw new EdictClaimCheckFetchException(
                    EdictClaimCheckFetchException.Reason.PayloadMissing,
                    key,
                    $"Claim-check payload not found for key '{key}'.");
            }
            return (byte[])result;
        }
        catch (NpgsqlException exception)
        {
            throw EdictPostgresStorageException.From(exception,
                $"GetAsync failed for claim-check table {_tableName} (key {key})");
        }
    }
}
