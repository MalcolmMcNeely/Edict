using Testcontainers.Azurite;

namespace Edict.Postgres.Tests;

/// <summary>
/// Assembly-scoped Azurite testcontainer for the AQS streams half of the
/// Postgres-persistence × AQS-streams conformance pairing. Mirrors
/// Edict.Azure.Tests' AzuriteAssemblyHost — same shared-Azurite +
/// per-fixture-namespacing model.
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
                    // Best-effort teardown.
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
