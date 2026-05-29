using Edict.Contracts.ClaimCheck;

using Npgsql;

using NpgsqlTypes;

namespace Edict.Postgres.ClaimCheck;

/// <summary>
/// Postgres-backed <see cref="IEdictClaimCheckStore"/> for the claim-check
/// escape hatch (ADR-0020). Append-only by design — the seam exposes no
/// delete, and Postgres has no per-row cap (TOAST handles large bytea via
/// lz4 compression). Key generation is the store's responsibility: each
/// payload lands at a fresh GUID so a missing-blob lookup at the receiver
/// surfaces as a dead-letter promotion (BlobMissing failure kind) rather
/// than a silent overwrite.
/// </summary>
public sealed class PostgresClaimCheckStore : IEdictClaimCheckStore
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
            throw new InvalidOperationException(
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
                throw new InvalidOperationException(
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
