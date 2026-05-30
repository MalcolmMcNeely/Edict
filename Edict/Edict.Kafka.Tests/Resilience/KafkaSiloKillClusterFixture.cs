using Confluent.Kafka;

using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Kafka;
using Edict.Kafka.Internal;
using Edict.Postgres;
using Edict.Postgres.TableStorage;
using Edict.Tests.Conformance;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

using Npgsql;

using Orleans;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;

using Testcontainers.Kafka;

namespace Edict.Kafka.Tests.Resilience;

// Owns its own Kafka container: KillSiloAsync + RestartSiloAsync is a brittle
// dance against any cluster fixture sharing the broker — the shared-consumer
// group offsets and Kafka client connections held by other tests would race.
// Single-silo with InitialSilosCount = 1 mirrors the KafkaClusterFixture's
// shape; multi-silo correctness stays out of this test surface.
// PartitionCountByStream pins the silo-kill streams to one partition so the
// Orleans queue balancer has exactly one QueueId to assign — restart is a
// straight re-Assign() on the same partition, with no rebalance ambiguity.
public sealed class KafkaSiloKillClusterFixture : IAsyncLifetime
{
    KafkaContainer _kafka = null!;
    string _adminConnectionString = "";
    string _databaseConnectionString = "";
    string _bootstrapServers = "";
    string _consumerGroup = "";
    NpgsqlDataSource _dataSource = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public string PostgresConnectionString => _databaseConnectionString;

    public NpgsqlDataSource PostgresDataSource => _dataSource;

    public string DeadLetterTableName { get; } = "edict_dead_letter";

    public string ClaimCheckTableName { get; } = "edict_claim_check";

    public async Task InitializeAsync()
    {
        _kafka = new KafkaBuilder().Build();
        await _kafka.StartAsync();

        var address = _kafka.GetBootstrapAddress();
        _bootstrapServers = address.StartsWith("PLAINTEXT://", StringComparison.Ordinal)
            ? address.Substring("PLAINTEXT://".Length)
            : address;
        _consumerGroup = $"edict-kafka-silokill-{Guid.NewGuid():N}";

        _adminConnectionString = await PostgresAssemblyHost.GetAdminConnectionStringAsync();
        var databaseName = $"edict_{Guid.NewGuid():N}";
        _databaseConnectionString =
            await PostgresDatabaseFactory.CreateDatabaseAsync(_adminConnectionString, databaseName);
        _dataSource = new NpgsqlDataSourceBuilder(_databaseConnectionString).Build();

        var context = new KafkaClusterContext(
            _bootstrapServers,
            _consumerGroup,
            _databaseConnectionString,
            DeadLetterTableName,
            ClaimCheckTableName,
            databaseName);
        _contextKey = KafkaClusterContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Properties[KafkaClusterContextRegistry.ContextKeyProperty] = _contextKey;
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (Cluster is not null)
        {
            await Cluster.DisposeAsync();
        }
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }
        KafkaClusterContextRegistry.Unregister(_contextKey);
        if (_kafka is not null)
        {
            await _kafka.DisposeAsync();
        }
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(OrderCommandHandler).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddAssembly(typeof(KafkaWireEnvelope).Assembly)
            .AddAssembly(typeof(KafkaSiloKillEvent).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[KafkaClusterContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException(
                    "ClusterContextKey missing from silo configuration.");
            var ctx = KafkaClusterContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Services.AddSingleton<IValidator<ValidateSkuCommand>, SkuRequiredValidator>();
            siloBuilder.Services.AddSingleton<IValidator<StateCheckCommand>, GrainStateRequiredValidator>();
            siloBuilder.AddEdict();
            siloBuilder.AddEdictKafkaStreams(o =>
            {
                o.BootstrapServers = ctx.KafkaBootstrapServers;
                o.ConsumerGroupId = ctx.KafkaConsumerGroup;
                o.PartitionCount = 4;
                // Single-partition silo-kill streams remove all queue-balancer
                // ambiguity: one QueueId per stream, deterministic re-Assign on
                // restart. Other streams provisioned by AppDomain discovery
                // keep the fleet default.
                o.PartitionCountByStream[KafkaSiloKillEvent.StreamName] = 1;
                o.PartitionCountByStream[KafkaSiloKillBatchEvent.StreamName] = 1;
                // Earliest so the new consumer on the restarted silo replays
                // anything written before its receiver finishes Assign() —
                // mirrors KafkaClusterFixture.
                o.AutoOffsetReset = AutoOffsetReset.Earliest;
            });
            siloBuilder.AddEdictPostgresPersistence(o =>
            {
                o.ConnectionString = ctx.PostgresConnectionString;
                o.DeadLetterTableName = ctx.DeadLetterTableName;
                o.ClaimCheckTableName = ctx.ClaimCheckTableName;
            });
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(
            IConfiguration configuration,
            IClientBuilder clientBuilder)
        {
            var key = configuration[KafkaClusterContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException(
                    "ClusterContextKey missing from client configuration.");
            var ctx = KafkaClusterContextRegistry.Get(key);

            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
            clientBuilder.Services.AddSingleton(
                new NpgsqlDataSourceBuilder(ctx.PostgresConnectionString).Build());
            clientBuilder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(serviceProvider =>
                new PostgresTableRepository<EdictDeadLetterEntry>(
                    serviceProvider.GetRequiredService<NpgsqlDataSource>(),
                    ctx.DeadLetterTableName,
                    serviceProvider.GetRequiredService<Serializer>()));
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class KafkaSiloKillCollection : ICollectionFixture<KafkaSiloKillClusterFixture>
{
    public const string Name = "KafkaSiloKill";
}
