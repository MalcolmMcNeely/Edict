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
            // Default max_connections=100 is too tight for parallel xUnit
            // collections: four cluster fixtures × up to 100 pooled per silo
            // plus the unit-test data source can oversubscribe the cap and
            // surface as `53300: sorry, too many clients already`. Mirror
            // the pattern KafkaPostgresSubstrate uses for the same reason.
            .WithCommand("-c", "max_connections=512")
            .Build();
        await container.StartAsync();
        return container;
    }
}
