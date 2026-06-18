using Npgsql;

namespace Edict.Postgres.Tests;

/// <summary>
/// Per-fixture database bring-up inside the shared Postgres testcontainer.
/// Each <see cref="PostgresClusterFixture"/> creates its own database and
/// hands back a connection string targeting it; isolation matches the
/// per-fixture-Guid-suffix model the Azure fixtures use for blob containers.
/// </summary>
static class PostgresDatabaseFactory
{
    public static async Task<string> CreateDatabaseAsync(string adminConnectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // CREATE DATABASE cannot run inside a transaction; identifier
        // quoting handles mixed-case names. Each fixture mints its own
        // name so collisions are impossible.
        command.CommandText = $"CREATE DATABASE \"{databaseName.Replace("\"", "\"\"")}\";";
        await command.ExecuteNonQueryAsync();

        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = databaseName,
            // Collections run serially, but a disposed collection's pooled
            // connections linger as idle server backends until Npgsql prunes
            // them (default ~300 s). With wall-clock waits gone the cadence is
            // fast enough that many collections' worth of un-pruned idle
            // backends overlap and oversubscribe the server, surfacing as
            // `53300: sorry, too many clients already`. A small pool plus an
            // aggressive idle-prune window keeps each collection's footprint
            // bounded and releases it within seconds of teardown. These flow
            // into both the test data source and the silo's Orleans AdoNet
            // PubSubStore/Reminders pool; the idle settings also carry into
            // Edict's own data source (which overrides only Max/MinPoolSize).
            MaxPoolSize = 30,
            MinPoolSize = 0,
            ConnectionIdleLifetime = 5,
            ConnectionPruningInterval = 1,
        };
        return builder.ConnectionString;
    }
}
