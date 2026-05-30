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

// Owns its own Kafka container instead of sharing KafkaAssemblyHost:
// pausing or restarting mid-test would corrupt every parallel collection
// running against the shared broker. Same Postgres testcontainer is reused
// from PostgresAssemblyHost because persistence is the dependency, not the
// substrate being faulted — the resilience suite owns the failure point.
public sealed class KafkaResilienceClusterFixture : IAsyncLifetime
{
    KafkaContainer _kafka = null!;
    string _adminConnectionString = "";
    string _databaseConnectionString = "";
    string _bootstrapServers = "";
    string _consumerGroup = "";
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

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
        _consumerGroup = $"edict-kafka-resilience-{Guid.NewGuid():N}";

        _adminConnectionString = await PostgresAssemblyHost.GetAdminConnectionStringAsync();
        var databaseName = $"edict_{Guid.NewGuid():N}";
        _databaseConnectionString =
            await PostgresDatabaseFactory.CreateDatabaseAsync(_adminConnectionString, databaseName);

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
        KafkaClusterContextRegistry.Unregister(_contextKey);
        if (_kafka is not null)
        {
            await _kafka.DisposeAsync();
        }
    }

    // Pause preserves the host port binding. Mirrors the Azurite resilience
    // suite's reasoning: stop+start re-binds the host port, which masks the
    // framework's reconnect behaviour behind a host-wiring artefact.
    public async Task PauseKafkaAsync() => await _kafka.PauseAsync();

    public async Task UnpauseKafkaAsync() => await _kafka.UnpauseAsync();

    // Tests call this on entry so the fixture starts from a known-good
    // baseline even if a previous test panicked mid pause.
    public async Task EnsureRunningAsync()
    {
        if (_kafka.State == DotNet.Testcontainers.Containers.TestcontainersStates.Paused)
        {
            await _kafka.UnpauseAsync();
        }
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(OrderCommandHandler).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddAssembly(typeof(KafkaWireEnvelope).Assembly)
            .AddAssembly(typeof(KafkaResilienceTestConsumer).Assembly)
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
                // Earliest so the receiver replays anything written before
                // the consumer-group member finished joining — same reasoning
                // as KafkaClusterFixture.
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
public sealed class KafkaResilienceCollection : ICollectionFixture<KafkaResilienceClusterFixture>
{
    public const string Name = "KafkaResilience";
}
