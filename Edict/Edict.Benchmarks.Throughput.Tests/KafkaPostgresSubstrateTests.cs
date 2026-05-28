using Confluent.Kafka;

using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core.Serialization;
using Edict.Postgres.TableStorage;
using Edict.Substrate;
using Edict.Substrate.KafkaPostgres;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class KafkaPostgresSubstrateTests
{
    [Fact]
    public async Task Runtime_CreateRowRepository_ReadsBackPreloadedBenchEventRow()
    {
        // Postgres mirror of the Azurite seam test — proves the harness's
        // generic factory call returns a PostgresTableRepository on Kafka +
        // Postgres and reads back a BenchEventRow seeded directly through the
        // write-store. The Events scenario's completion poll rides this exact
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
        await using var sp = services.BuildServiceProvider();
        var serializer = sp.GetRequiredService<Serializer>();

        await using var dataSource = new NpgsqlDataSourceBuilder(runtime.PostgresConnectionString).Build();
        var factory = new PostgresTableWriteStoreFactory(dataSource, serializer);
        var store = await factory.CreateAsync<BenchEventRow>(BenchProjectionBuilder.TableNameLiteral);
        var partitionKey = Guid.NewGuid().ToString("N");
        var rowKey = Guid.NewGuid().ToString("N");
        await store.UpsertAsync(partitionKey, rowKey, new BenchEventRow());

        var repository = runtime.CreateRowRepository<BenchEventRow>(
            sp, BenchProjectionBuilder.TableNameLiteral);

        var row = await repository.GetAsync(partitionKey, rowKey);

        Assert.NotNull(row);
        Assert.IsType<PostgresTableRepository<BenchEventRow>>(repository);
    }

    [Fact]
    public async Task Runtime_SiloCallback_WiresKafkaStreamsAtRuntimeBootstrap_AndPostgresDeadLetterRepoReadsBackSeededRow()
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

                // Persistence half: seed a dead-letter row through Postgres and
                // assert the substrate-published repository reads it back.
                // Postgres seeding needs the silo's Serializer for the on-disk
                // payload format, so seeding happens post-deploy (unlike the
                // Azurite mirror which seeds raw TableEntity rows).
                var serializer = cluster.ServiceProvider.GetRequiredService<Serializer>();
                await using var dataSource = new NpgsqlDataSourceBuilder(runtime.PostgresConnectionString).Build();
                var factory = new PostgresTableWriteStoreFactory(
                    dataSource, serializer);
                var store = await factory.CreateAsync<EdictDeadLetterEntry>(
                    KafkaPostgresSubstrate.DeadLetterTableName);
                var partitionKey = Guid.NewGuid().ToString("N");
                var rowKey = Guid.NewGuid().ToString("N");
                await store.UpsertAsync(partitionKey, rowKey, new EdictDeadLetterEntry
                {
                    EntryId = Guid.NewGuid(),
                    Kind = "PublishEvent",
                    AttemptCount = 3,
                    DeadLetteredAt = DateTimeOffset.UtcNow,
                    SourceGrainKey = "grain-key",
                    SourceGrainType = "Grain.Type",
                    EffectTarget = "stream/EventType",
                    FailureKind = EdictDeadLetterFailureKind.EffectFailure,
                });

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
                Microsoft.Extensions.Configuration.IConfiguration configuration,
                IClientBuilder clientBuilder)
            {
                var runtime = Current ?? throw new InvalidOperationException(
                    "ActiveSubstrateRuntime.Current was null when the client configurator ran.");
                runtime.ConfigureClient(clientBuilder);
            }
        }
    }
}
