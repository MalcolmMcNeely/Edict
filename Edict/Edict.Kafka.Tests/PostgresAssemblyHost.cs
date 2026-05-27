using Testcontainers.PostgreSql;

namespace Edict.Kafka.Tests;

/// <summary>
/// Assembly-scoped Postgres testcontainer for the persistence half of the
/// Kafka-streams × Postgres-persistence conformance pairing. Mirrors
/// <c>Edict.Postgres.Tests/PostgresAssemblyHost.cs</c>.
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
                    // Best-effort teardown.
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
            .WithImage("postgres:16-alpine")
            .Build();
        await container.StartAsync();
        return container;
    }
}
