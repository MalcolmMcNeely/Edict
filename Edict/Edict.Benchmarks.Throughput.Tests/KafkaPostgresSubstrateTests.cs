using Confluent.Kafka;

using Edict.Benchmarks.Throughput.Workload;
using Edict.Core.Serialization;
using Edict.Postgres.TableStorage;
using Edict.Substrate;
using Edict.Substrate.KafkaPostgres;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

using Npgsql;

using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class KafkaPostgresSubstrateTests
{
    [Fact]
    public async Task Runtime_CreateRowStore_ReadsBackPreloadedBenchEventRow()
    {
        // Postgres mirror of the Azurite seam test — proves the harness's
        // generic CreateRowStore<T> call returns a PostgresTableWriteStore on
        // Kafka + Postgres and reads back a BenchEventRow seeded directly through
        // the write-store. The Events scenario's completion poll rides this exact
        // path during a Kafka × Postgres throughput sweep.
        var substrate = new KafkaPostgresSubstrate();
        await using var runtime = (KafkaPostgresSubstrateRuntime)await substrate.StartAsync(CancellationToken.None);

        // Minimal IServiceProvider with the harness assembly's [GenerateSerializer]
        // codegen — no TestCluster needed for a pure write+read round-trip on
        // BenchEventRow.
        var services = new ServiceCollection();
        services.AddSerializer(s => s
            .AddAssembly(typeof(BenchEventRow).Assembly)
            .AddEdictContractSerializer());
        await using var serviceProvider = services.BuildServiceProvider();
        var serializer = serviceProvider.GetRequiredService<Serializer>();

        await using var dataSource = new NpgsqlDataSourceBuilder(runtime.PostgresConnectionString).Build();
        var factory = new PostgresTableWriteStoreFactory(dataSource, serializer);
        var store = await factory.CreateAsync<BenchEventRow>(BenchProjectionBuilder.TableNameLiteral);
        var partitionKey = Guid.NewGuid().ToString("N");
        var rowKey = Guid.NewGuid().ToString("N");
        await store.UpsertAsync(partitionKey, rowKey, new BenchEventRow());

        var harnessStore = runtime.CreateRowStore<BenchEventRow>(
            serviceProvider, BenchProjectionBuilder.TableNameLiteral);

        var row = await harnessStore.GetAsync(partitionKey, rowKey);

        Assert.NotNull(row);
        Assert.IsType<PostgresTableWriteStore<BenchEventRow>>(harnessStore);
    }

    [Fact]
    public async Task Runtime_SiloCallback_WiresKafkaStreamsAtRuntimeBootstrap()
    {
        var substrate = new KafkaPostgresSubstrate();
        await using var runtime = (KafkaPostgresSubstrateRuntime)await substrate.StartAsync(CancellationToken.None);

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
                // Streams half: the bootstrap address the substrate published
                // points at a live broker. A metadata fetch is the cheapest
                // proof — no topic creation, no produce/consume — and avoids
                // reaching into the out-of-process silo's ServiceProvider for
                // EdictKafkaStreamsOptions. The cluster also deployed without
                // throwing, which already proved AddEdictKafkaStreams ran with
                // a non-empty BootstrapServers (the extension validates that).
                using (var admin = new AdminClientBuilder(
                    new AdminClientConfig { BootstrapServers = runtime.BootstrapServers })
                    .Build())
                {
                    var metadata = admin.GetMetadata(TimeSpan.FromSeconds(10));
                    Assert.NotEmpty(metadata.Brokers);
                }
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
    public async Task ClosedLoopMode_AppliesEarliestAutoOffsetReset()
    {
        // Default mode keeps the existing fresh-group replay behaviour the
        // closed-loop sweep relies on so warmup-window backlog is replayed
        // into the measurement consumer rather than silently dropped.
        var substrate = new KafkaPostgresSubstrate();
        await using var runtime = (KafkaPostgresSubstrateRuntime)await substrate.StartAsync(CancellationToken.None);

        Assert.Equal(AutoOffsetReset.Earliest, runtime.KafkaAutoOffsetReset);
    }

    [Fact]
    public async Task SaturationMode_AppliesLatestAutoOffsetReset()
    {
        // Saturation pass measures count-at-window-end on a fresh consumer
        // group; replaying history would inflate EPS by counting events the
        // producer issued during warmup. Latest is the substrate-level guard.
        var substrate = new KafkaPostgresSubstrate();
        await using var runtime = (KafkaPostgresSubstrateRuntime)await substrate.StartAsync(
            CancellationToken.None, SubstrateStartMode.Saturation);

        Assert.Equal(AutoOffsetReset.Latest, runtime.KafkaAutoOffsetReset);
    }

    static class ActiveSubstrateRuntime
    {
        public static ISubstrateRuntime? Current { get; set; }

        public sealed class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                var runtime = Current ?? throw new InvalidOperationException(
                    "ActiveSubstrateRuntime.Current was null when the silo configurator ran.");
                runtime.ConfigureSilo(siloBuilder);
            }
        }

        public sealed class ClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(
                IConfiguration configuration,
                IClientBuilder clientBuilder)
            {
                var runtime = Current ?? throw new InvalidOperationException(
                    "ActiveSubstrateRuntime.Current was null when the client configurator ran.");
                runtime.ConfigureClient(clientBuilder);
            }
        }
    }
}
