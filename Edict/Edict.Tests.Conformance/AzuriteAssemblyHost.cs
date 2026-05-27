using Testcontainers.Azurite;

namespace Edict.Tests.Conformance;

/// <summary>
/// Assembly-scoped Azurite testcontainer shared by every provider test
/// project (Azure, Postgres, Kafka). Each xUnit assembly process gets one
/// Azurite container — fixtures namespace their own table/blob/queue names
/// inside that shared container so parallel collections do not collide.
/// Teardown is on <see cref="AppDomain.ProcessExit"/> because xUnit
/// collections may overlap and a fixture-scoped dispose would strand the
/// next collection.
/// </summary>
public static class AzuriteAssemblyHost
{
    static readonly Lazy<Task<AzuriteContainer>> _container =
        new(StartAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    static AzuriteAssemblyHost()
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
                    // Best-effort teardown — the process is exiting anyway.
                }
            }
        };
    }

    public static async Task<string> GetConnectionStringAsync()
    {
        var container = await _container.Value;
        return container.GetConnectionString();
    }

    static async Task<AzuriteContainer> StartAsync()
    {
        var container = new AzuriteBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:3.35.0")
            .WithCreateParameterModifier(p =>
            {
                p.Cmd ??= [];
                p.Cmd.Add("--skipApiVersionCheck");
            })
            .Build();
        await container.StartAsync();
        return container;
    }
}
