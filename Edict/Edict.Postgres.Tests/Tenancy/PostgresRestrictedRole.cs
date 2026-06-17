using Edict.Postgres.TableStorage;

using Npgsql;

namespace Edict.Postgres.Tests.Tenancy;

// Builds a non-superuser login role and a data source that connects as it, the only
// context in which Postgres enforces Row-Level Security: the superuser the Testcontainers
// image and the silo connect as bypasses RLS entirely, and the table owner bypasses it
// unless FORCE is set, so the policy only bites for a distinct unprivileged role. The role
// is granted nothing beyond SELECT on the named tables — the read surface the backstop
// guards. The role name is minted per call so the cluster-global role namespace never
// collides across fixtures sharing the one testcontainer.
static class PostgresRestrictedRole
{
    public static async Task<NpgsqlDataSource> BuildAsync(
        NpgsqlDataSource superuserDataSource, string roleName, string password, params string[] tableNames)
    {
        var quotedRole = PostgresTableSchema.QuoteIdentifier(roleName);
        await using (var connection = await superuserDataSource.OpenConnectionAsync())
        {
            await using var create = connection.CreateCommand();
            create.CommandText =
                $"CREATE ROLE {quotedRole} LOGIN PASSWORD '{password}';" +
                $"GRANT USAGE ON SCHEMA public TO {quotedRole};";
            await create.ExecuteNonQueryAsync();

            foreach (var tableName in tableNames)
            {
                await using var grant = connection.CreateCommand();
                grant.CommandText =
                    $"GRANT SELECT ON {PostgresTableSchema.QuoteIdentifier(tableName)} TO {quotedRole};";
                await grant.ExecuteNonQueryAsync();
            }
        }

        var builder = new NpgsqlConnectionStringBuilder(superuserDataSource.ConnectionString)
        {
            Username = roleName,
            Password = password,
        };
        return new NpgsqlDataSourceBuilder(builder.ConnectionString).Build();
    }
}
