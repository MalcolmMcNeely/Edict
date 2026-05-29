using Azure.Data.Tables;

using Edict.Azure.Persistence.TableStorage;
using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.DeadLetter;
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
    public async Task Runtime_CreateRowRepository_ReadsBackPreloadedBenchEventRow()
    {
        // The harness drives its workload-specific row registration
        // through the substrate runtime's generic CreateRowRepository<T>
        // seam. The substrate library still owns no workload types; the
        // factory picks the substrate-correct repo (AzureTableRepository
        // on Azurite, PostgresTableRepository on Kafka+Postgres) from one
        // call site in the harness.
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

        var repository = runtime.CreateRowRepository<BenchEventRow>(
            serviceProvider, BenchProjectionBuilder.TableNameLiteral);

        var row = await repository.GetAsync(partitionKey, rowKey);

        Assert.NotNull(row);
        Assert.IsType<AzureTableRepository<BenchEventRow>>(repository);
    }

    [Fact]
    public async Task HarnessWiredOverRuntime_ResolvesBenchEventRowRepositoryThatReadsBackPreloadedRow()
    {
        // The Events scenario's issuer polls
        // IEdictTableRepository<BenchEventRow> for completion. The
        // substrate library no longer owns this workload-specific repo;
        // the harness layers it on top of the substrate's ConfigureClient.
        // Same end-to-end wiring, expressed at the layer that actually
        // owns the workload.
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
                var repository = cluster.Client.ServiceProvider
                    .GetRequiredService<IEdictTableRepository<BenchEventRow>>();

                var row = await repository.GetAsync(partitionKey, rowKey);

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
    public async Task Runtime_SiloCallback_ResolvesTableRepositoryThatReadsBackPreloadedRow()
    {
        var substrate = new AzuriteSubstrate();
        await using var runtime = (AzuriteSubstrateRuntime)await substrate.StartAsync(CancellationToken.None);

        // Pre-seed a row directly through Azurite — substrate's persistence
        // wiring will register the repository against the same table.
        var preloadTable = new TableServiceClient(runtime.ConnectionString)
            .GetTableClient(AzuriteSubstrate.DeadLetterTableName);
        await preloadTable.CreateIfNotExistsAsync();
        var partitionKey = Guid.NewGuid().ToString("N");
        var rowKey = Guid.NewGuid().ToString("N");
        await preloadTable.AddEntityAsync(new TableEntity(partitionKey, rowKey)
        {
            { nameof(EdictDeadLetterEntry.EntryId), Guid.NewGuid() },
            { nameof(EdictDeadLetterEntry.Kind), "PublishEvent" },
            { nameof(EdictDeadLetterEntry.AttemptCount), 3 },
            { nameof(EdictDeadLetterEntry.DeadLetteredAt), DateTimeOffset.UtcNow },
            { nameof(EdictDeadLetterEntry.SourceGrainKey), "grain-key" },
            { nameof(EdictDeadLetterEntry.SourceGrainType), "Grain.Type" },
            { nameof(EdictDeadLetterEntry.EffectTarget), "stream/EventType" },
            { nameof(EdictDeadLetterEntry.FailureKind), (int)EdictDeadLetterFailureKind.EffectFailure },
        });

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
                var repository = cluster.ServiceProvider
                    .GetRequiredService<IEdictTableRepository<EdictDeadLetterEntry>>();

                var row = await repository.GetAsync(partitionKey, rowKey);

                Assert.NotNull(row);
                Assert.Equal("PublishEvent", row!.Kind);
                Assert.Equal(3, row.AttemptCount);
                Assert.Equal("grain-key", row.SourceGrainKey);
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
                // workload row repo lives in the harness, picked through the
                // runtime's CreateRowRepository<T> seam.
                clientBuilder.Services.AddSingleton<IEdictTableRepository<BenchEventRow>>(serviceProvider =>
                    runtime.CreateRowRepository<BenchEventRow>(serviceProvider, BenchProjectionBuilder.TableNameLiteral));
            }
        }
    }
}
