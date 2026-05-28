using Npgsql;

namespace Edict.Postgres.TableStorage;

/// <summary>
/// Shared DDL helper for the per-projection Postgres table. Schema is fixed:
/// <c>(partition_key TEXT, row_key TEXT, payload BYTEA, etag TEXT)</c> with
/// composite PK <c>(partition_key, row_key)</c>. <c>CREATE TABLE IF NOT
/// EXISTS</c> so factory bring-up is idempotent and parallel test fixtures
/// don't race on each other.
/// </summary>
internal static class PostgresTableSchema
{
    internal static async Task EnsureProjectionTableAsync(
        NpgsqlDataSource dataSource,
        string tableName,
        CancellationToken ct)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"CREATE TABLE IF NOT EXISTS {QuoteIdentifier(tableName)} (" +
                "partition_key TEXT NOT NULL, " +
                "row_key TEXT NOT NULL, " +
                "payload BYTEA NOT NULL, " +
                "etag TEXT NOT NULL, " +
                "PRIMARY KEY (partition_key, row_key)" +
                ");";
            await command.ExecuteNonQueryAsync(ct);
        }
        catch (NpgsqlException ex)
        {
            throw EdictPostgresStorageException.From(ex,
                $"EnsureProjectionTableAsync failed for {tableName}");
        }
    }

    /// <summary>
    /// Double-quote a Postgres identifier and escape embedded quotes. Table
    /// names come from framework code (constants or option strings), not
    /// untrusted input, but quoting keeps mixed-case identifiers ("OrderProjection")
    /// case-preserving instead of folded to lowercase by Postgres.
    /// </summary>
    internal static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
