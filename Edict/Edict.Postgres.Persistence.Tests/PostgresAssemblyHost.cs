using Testcontainers.PostgreSql;

namespace Edict.Postgres.Persistence.Tests;

/// <summary>
/// Assembly-scoped Postgres testcontainer, shared by every persistence-axis
/// fixture. Each fixture creates its own database inside the container so
/// collections do not race on each other's tables. Teardown on
/// <c>ProcessExit</c> matches the cross-battery's <c>PostgresAssemblyHost</c>.
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
            // The battery runs serially, so the six fixture shapes never deploy
            // at once — but each silo's pool plus PubSubStore/Reminders can still
            // open dozens of connections, so lift the default 100 cap to leave
            // generous headroom. Mirrors the cross-battery host.
            .WithCommand("-c", "max_connections=512")
            .Build();
        await container.StartAsync();
        return container;
    }
}
