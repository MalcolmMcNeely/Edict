using Azure.Data.Tables;

using Microsoft.Extensions.DependencyInjection;

namespace Edict.Azure.Tests;

// a DI-registered TableServiceClient / BlobServiceClient /
// QueueServiceClient takes precedence over the options-bag instance. This
// lets a power user share one set of Azure SDK clients across multiple
// features (AddAzureClients() pattern) without double-registering them on
// Edict's options bag. The resolution lives in
// AddEdictAzurePersistence as a lazy `sp.GetService<T>() ?? options.T`
// chain — these tests reproduce the chain in isolation via a keyed factory
// so the resolution can be asserted without spinning up a silo.
public sealed class AzureClientDIOverrideTests
{
    const string ResolvedKey = "edict-resolved";

    [Fact]
    public void TableServiceClient_RegisteredInDi_ShouldWinOverOptionsBagInstance()
    {
        var diClient = new TableServiceClient("UseDevelopmentStorage=true");
        var bagClient = new TableServiceClient("UseDevelopmentStorage=true");

        var services = new ServiceCollection();
        services.AddSingleton(diClient);
        services.AddKeyedSingleton<TableServiceClient>(ResolvedKey, (sp, _) =>
            sp.GetService<TableServiceClient>()
                ?? bagClient
                ?? throw new InvalidOperationException("unreachable"));

        var resolved = services.BuildServiceProvider().GetRequiredKeyedService<TableServiceClient>(ResolvedKey);

        Assert.Same(diClient, resolved);
    }

    [Fact]
    public void TableServiceClient_NotInDi_ShouldFallBackToOptionsBag()
    {
        var bagClient = new TableServiceClient("UseDevelopmentStorage=true");

        var services = new ServiceCollection();
        services.AddKeyedSingleton<TableServiceClient>(ResolvedKey, (sp, _) =>
            sp.GetService<TableServiceClient>()
                ?? bagClient
                ?? throw new InvalidOperationException("unreachable"));

        var resolved = services.BuildServiceProvider().GetRequiredKeyedService<TableServiceClient>(ResolvedKey);

        Assert.Same(bagClient, resolved);
    }
}
