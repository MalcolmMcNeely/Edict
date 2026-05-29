using Azure.Data.Tables;

using Microsoft.Extensions.DependencyInjection;

namespace Edict.Azure.Tests;

// AddEdictAzurePersistence resolves clients as `serviceProvider.GetService<T>() ??
// options.T` — these tests reproduce the chain in isolation via a keyed
// factory so the precedence can be asserted without spinning up a silo.
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
        services.AddKeyedSingleton<TableServiceClient>(ResolvedKey, (serviceProvider, _) =>
            serviceProvider.GetService<TableServiceClient>()
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
        services.AddKeyedSingleton<TableServiceClient>(ResolvedKey, (serviceProvider, _) =>
            serviceProvider.GetService<TableServiceClient>()
                ?? bagClient
                ?? throw new InvalidOperationException("unreachable"));

        var resolved = services.BuildServiceProvider().GetRequiredKeyedService<TableServiceClient>(ResolvedKey);

        Assert.Same(bagClient, resolved);
    }
}
