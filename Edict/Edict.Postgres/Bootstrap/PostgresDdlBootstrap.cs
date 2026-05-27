using System.Reflection;

using Npgsql;

namespace Edict.Postgres.Bootstrap;

/// <summary>
/// Runs the embedded Orleans + Edict DDL scripts against the configured
/// connection string at silo wiring time. Idempotent by construction:
/// Edict-owned scripts use <c>CREATE TABLE IF NOT EXISTS</c>; Orleans scripts
/// are skipped when their canonical table already exists.
/// </summary>
internal static class PostgresDdlBootstrap
{
    const string MainScriptResource = "Edict.Postgres.Sql.PostgreSQL-Main.sql";
    const string PersistenceScriptResource = "Edict.Postgres.Sql.PostgreSQL-Persistence.sql";
    const string RemindersScriptResource = "Edict.Postgres.Sql.PostgreSQL-Reminders.sql";
    const string EdictBootstrapResource = "Edict.Postgres.Sql.EdictPostgres-Bootstrap.sql";

    internal static void Run(string connectionString)
    {
        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();

        // Orleans scripts are gated by their canonical table to make rerun a
        // no-op. The scripts use `CREATE TABLE`, not `CREATE TABLE IF NOT
        // EXISTS`, and insert into OrleansQuery — a duplicate-key on a second
        // run would crash the silo.
        RunIfTableMissing(connection, "orleansquery", MainScriptResource);
        RunIfTableMissing(connection, "orleansstorage", PersistenceScriptResource);
        RunIfTableMissing(connection, "orleansreminderstable", RemindersScriptResource);

        // Edict-owned DDL — uses CREATE TABLE IF NOT EXISTS so safe to rerun.
        Execute(connection, ReadResource(EdictBootstrapResource));
    }

    static void RunIfTableMissing(NpgsqlConnection connection, string tableName, string resource)
    {
        using var probe = connection.CreateCommand();
        probe.CommandText = "SELECT to_regclass(@table_name)::text;";
        probe.Parameters.AddWithValue("table_name", tableName);
        var result = probe.ExecuteScalar();
        if (result is null || result is DBNull)
        {
            Execute(connection, ReadResource(resource));
        }
    }

    static void Execute(NpgsqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static string ReadResource(string name)
    {
        var assembly = typeof(PostgresDdlBootstrap).Assembly;
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(
                $"Embedded SQL resource '{name}' not found in {assembly.GetName().Name}. " +
                "Confirm <EmbeddedResource Include=\"Sql\\*.sql\" /> in the .csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
