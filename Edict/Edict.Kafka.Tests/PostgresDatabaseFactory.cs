using Npgsql;

namespace Edict.Kafka.Tests;

/// <summary>
/// Per-fixture database bring-up inside the shared Postgres testcontainer.
/// Each fixture creates its own database for isolation. Mirrors
/// <c>Edict.Postgres.Tests/PostgresDatabaseFactory.cs</c>.
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
