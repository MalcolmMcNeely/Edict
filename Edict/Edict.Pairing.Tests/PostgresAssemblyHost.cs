using Testcontainers.PostgreSql;

namespace Edict.Pairing.Tests;

/// <summary>
/// Assembly-scoped Postgres testcontainer for the Kafka+Postgres pairing smoke.
/// Each fixture creates its own database inside the container so the two pairing
/// scenarios never share grain-state, dead-letter, or claim-check tables.
/// Teardown on <c>ProcessExit</c> matches the provider suites' assembly hosts.
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
            .Build();
        await container.StartAsync();
        return container;
    }
}
