using Npgsql;

namespace Edict.Pairing.Tests;

/// <summary>
/// Per-fixture database bring-up inside the shared Postgres testcontainer, so the
/// Kafka+Postgres pairing scenarios never share tables.
/// </summary>
static class PostgresDatabaseFactory
{
    public static async Task<string> CreateDatabaseAsync(string adminConnectionString, string databaseName)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName.Replace("\"", "\"\"")}\";";
        await command.ExecuteNonQueryAsync();

        var builder = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Database = databaseName,
        };
        return builder.ConnectionString;
    }
}
