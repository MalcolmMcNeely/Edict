using Testcontainers.Azurite;

namespace Edict.Azure.Tests;

/// <summary>
/// Assembly-scoped Azurite container: one Testcontainers Azurite is started
/// lazily on first request and shared by every cluster fixture and self-managed
/// Azurite test in <c>Edict.Azure.Tests</c>. Container teardown is wired to
/// <see cref="AppDomain.ProcessExit"/> rather than per-fixture <c>DisposeAsync</c>
/// because xUnit collections may overlap and a fixture-scoped dispose would
/// strand the next collection. The leak is bounded to a single Azurite process
/// for the lifetime of the test assembly. (ADR 0029 — one Azurite per assembly.)
/// </summary>
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
