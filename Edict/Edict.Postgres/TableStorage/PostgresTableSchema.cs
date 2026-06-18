using Edict.Postgres.Tenancy;

using Npgsql;

namespace Edict.Postgres.TableStorage;

/// <summary>
/// Shared DDL helper for the per-projection Postgres table. Schema is fixed:
/// <c>(partition_key TEXT, row_key TEXT, payload BYTEA, etag TEXT)</c> with
/// composite PK <c>(partition_key, row_key)</c>. Bring-up is idempotent and
/// safe under concurrent first-writers to the same table — see
/// <see cref="EnsureProjectionTableAsync"/> for the lock that makes it so.
/// </summary>
internal static class PostgresTableSchema
{
    internal static async Task EnsureProjectionTableAsync(
        NpgsqlDataSource dataSource,
        string tableName,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            // CREATE TABLE IF NOT EXISTS is not concurrency-safe in Postgres: two sessions
            // creating the same table can both pass the existence check and then collide
            // inserting the table's implicit row type into pg_type (unique_violation on
            // pg_type_typname_nsp_index). Multiple tenants first-writing the same projection
            // table do exactly that. A transaction-scoped advisory lock keyed on the table
            // name serialises concurrent creators — held until commit, so it also covers the
            // row-security policy DDL below.
            await using (var lockCommand = connection.CreateCommand())
            {
                lockCommand.Transaction = transaction;
                lockCommand.CommandText = "SELECT pg_advisory_xact_lock(hashtext(@lock_key)::bigint);";
                lockCommand.Parameters.AddWithValue("lock_key", "edict_ensure_table:" + tableName);
                await lockCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    $"CREATE TABLE IF NOT EXISTS {QuoteIdentifier(tableName)} (" +
                    "partition_key TEXT NOT NULL, " +
                    "row_key TEXT NOT NULL, " +
                    "payload BYTEA NOT NULL, " +
                    "etag TEXT NOT NULL, " +
                    "PRIMARY KEY (partition_key, row_key)" +
                    ");" +
                    // The partition key carries the tenant as its {tenant}|... prefix (the
                    // whole value for the tenant-as-partition shape), so a row-security policy
                    // can confine a tenant read to its own partition at the database layer.
                    PostgresTenantRowSecurity.PolicySql(tableName, keyColumn: "partition_key");
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw EdictPostgresStorageException.From(exception,
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
