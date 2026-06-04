using Npgsql;

namespace Edict.Postgres.Persistence.Tests;

/// <summary>
/// Per-fixture database bring-up inside the shared Postgres testcontainer. Each
/// persistence-axis fixture creates its own database and hands back a connection
/// string targeting it, so the serial fixture shapes never share grain-state,
/// dead-letter, or claim-check tables.
/// </summary>
static class PostgresDatabaseFactory
{
    public static async Task<string> CreateDatabaseAsync(string adminConnectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        // CREATE DATABASE cannot run inside a transaction; identifier quoting
        // handles mixed-case names. Each fixture mints its own name so
        // collisions are impossible.
        command.CommandText = $"CREATE DATABASE \"{databaseName.Replace("\"", "\"\"")}\";";
        await command.ExecuteNonQueryAsync();

        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = databaseName,
        };
        return builder.ConnectionString;
    }
}
