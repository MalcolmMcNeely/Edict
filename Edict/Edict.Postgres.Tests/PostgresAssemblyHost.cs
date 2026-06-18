using Testcontainers.PostgreSql;

namespace Edict.Postgres.Tests;

/// <summary>
/// Assembly-scoped Postgres testcontainer, shared by every fixture. Each
/// fixture creates its own database inside the container so collections do
/// not race on each other's tables. Teardown on <c>ProcessExit</c> matches
/// <c>AzuriteAssemblyHost</c>.
/// </summary>
static class PostgresAssemblyHost
{
    static readonly Lazy<Task<PostgreSqlContainer>> _container =
        new(StartAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    static PostgresAssemblyHost()
    {
        AppDomain.CurrentDomain.ProcessExit += async (_, _) =>
        {
            if (_container.IsValueCreated)
            {
                try
                {
                    var container = await _container.Value;
                    await container.DisposeAsync();
                }
                catch
                {
                }
            }
        };
    }

    public static async Task<string> GetAdminConnectionStringAsync()
    {
        var container = await _container.Value;
        return container.GetConnectionString();
    }

    static async Task<PostgreSqlContainer> StartAsync()
    {
        var container = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            // Headroom above Postgres' default 100 cap. The real control on the
            // `53300: sorry, too many clients already` failure is the bounded,
            // fast-pruned per-fixture pools wired in PostgresDatabaseFactory and
            // PostgresPersistenceFixtureBase; this generous server ceiling just
            // absorbs the brief overlap as a disposed collection's idle backends
            // drain. Mirror the pattern KafkaPostgresSubstrate uses.
            .WithCommand("-c", "max_connections=512")
            .Build();
        await container.StartAsync();
        return container;
    }
}
