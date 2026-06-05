using Azure.Data.Tables;

using Edict.Azure.Persistence.TableStorage;
using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.TableStorage;
using Edict.Substrate;
using Edict.Substrate.Azurite;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

using Orleans.Hosting;
using Orleans.TestingHost;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class AzuriteSubstrateTests
{
    [Fact]
    public async Task Runtime_CreateRowStore_ReadsBackPreloadedBenchEventRow()
    {
        // The harness drives its workload-specific row registration through the
        // substrate runtime's generic CreateRowStore<T> seam. The substrate
        // library still owns no workload types; the seam picks the
        // substrate-correct store (AzureTableWriteStore on Azurite,
        // PostgresTableWriteStore on Kafka+Postgres) from one call site.
        var substrate = new AzuriteSubstrate();
        await using var runtime = (AzuriteSubstrateRuntime)await substrate.StartAsync(CancellationToken.None);

        var preloadTable = runtime.TableClient.GetTableClient(BenchProjectionBuilder.TableNameLiteral);
        await preloadTable.CreateIfNotExistsAsync();
        var partitionKey = Guid.NewGuid().ToString("N");
        var rowKey = Guid.NewGuid().ToString("N");
        await preloadTable.AddEntityAsync(new TableEntity(partitionKey, rowKey));

        var services = new ServiceCollection();
        services.AddSingleton(runtime.TableClient);
        await using var serviceProvider = services.BuildServiceProvider();

        var store = runtime.CreateRowStore<BenchEventRow>(
            serviceProvider, BenchProjectionBuilder.TableNameLiteral);

        var row = await store.GetAsync(partitionKey, rowKey);

        Assert.NotNull(row);
        Assert.IsType<AzureTableWriteStore<BenchEventRow>>(store);
    }

    [Fact]
    public async Task HarnessWiredOverRuntime_ResolvesBenchEventRowStoreThatReadsBackPreloadedRow()
    {
        // The Events scenario's issuer polls IEdictTableWriteStore<BenchEventRow>
        // (store-direct, off-grain — the right shape for a latency measurement)
        // for completion. The substrate library no longer owns this
        // workload-specific store; the harness layers it on top of the
        // substrate's ConfigureClient.
        var substrate = new AzuriteSubstrate();
        await using var runtime = (AzuriteSubstrateRuntime)await substrate.StartAsync(CancellationToken.None);

        var preloadTable = new TableServiceClient(runtime.ConnectionString)
            .GetTableClient(BenchProjectionBuilder.TableNameLiteral);
        await preloadTable.CreateIfNotExistsAsync();
        var partitionKey = Guid.NewGuid().ToString("N");
        var rowKey = Guid.NewGuid().ToString("N");
        await preloadTable.AddEntityAsync(new TableEntity(partitionKey, rowKey));

        ActiveSubstrateRuntime.Current = runtime;
        try
        {
            var builder = new TestClusterBuilder();
            builder.AddSiloBuilderConfigurator<ActiveSubstrateRuntime.SiloConfigurator>();
            builder.AddClientBuilderConfigurator<ActiveSubstrateRuntime.ClientConfigurator>();
            var cluster = builder.Build();
            await cluster.DeployAsync();
            try
            {
                var store = cluster.Client.ServiceProvider
                    .GetRequiredService<IEdictTableWriteStore<BenchEventRow>>();

                var row = await store.GetAsync(partitionKey, rowKey);

                Assert.NotNull(row);
            }
            finally
            {
                await cluster.DisposeAsync();
            }
        }
        finally
        {
            ActiveSubstrateRuntime.Current = null;
        }
    }

    [Fact]
    public async Task SaturationMode_IsAcceptedWithoutFailure()
    {
        // Azurite has no Earliest/Latest analogue on the Azure Queue stream
        // provider — the signal is a no-op here. The substrate must accept the
        // mode argument without throwing so the harness can drive it uniformly
        // across providers.
        var substrate = new AzuriteSubstrate();
        await using var runtime = await substrate.StartAsync(
            CancellationToken.None, SubstrateStartMode.Saturation);

        Assert.NotNull(runtime);
    }

    static class ActiveSubstrateRuntime
    {
        public static ISubstrateRuntime? Current { get; set; }

        public sealed class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                var runtime = Current ?? throw new InvalidOperationException("Substrate runtime not set");
                runtime.ConfigureSilo(siloBuilder);
            }
        }

        public sealed class ClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(
                IConfiguration configuration,
                IClientBuilder clientBuilder)
            {
                var runtime = Current ?? throw new InvalidOperationException("Substrate runtime not set");
                runtime.ConfigureClient(clientBuilder);
                // Mirrors the production ActiveRuntime.ClientConfigurator —
                // workload row store lives in the harness, picked through the
                // runtime's CreateRowStore<T> seam.
                clientBuilder.Services.AddSingleton<IEdictTableWriteStore<BenchEventRow>>(serviceProvider =>
                    runtime.CreateRowStore<BenchEventRow>(serviceProvider, BenchProjectionBuilder.TableNameLiteral));
            }
        }
    }
}
