using Testcontainers.Azurite;

namespace Edict.Azure.Tests;

// Assembly-scoped Azurite container, shared by every cluster fixture and
// self-managed Azurite test. Teardown is on ProcessExit because xUnit
// collections may overlap and a fixture-scoped dispose would strand the
// next collection.
static class AzuriteAssemblyHost
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
