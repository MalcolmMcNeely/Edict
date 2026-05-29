using Confluent.Kafka;

using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Kafka.Wire;
using Edict.Postgres;
using Edict.Postgres.TableStorage;
using Edict.Tests.Conformance;
using Edict.Tests.Conformance.Outbox;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orleans;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Kafka.Tests.DeadLetter;

/// <summary>
/// Conformance fixture wiring Kafka streams + Postgres persistence with the
/// process-wide flippable <see cref="ControllableOutboxExecutor"/> in place of
/// the framework's default <see cref="PublishEventExecutor"/>. Drives the
/// dead-letter promotion path on Kafka: the executor exhausts its 2 attempts,
/// the projection then writes the <c>EdictDeadLetterEntry</c> row through the
/// healed publisher. Mirrors <c>PostgresOutboxControllableExecutorFixture</c>;
/// only the streams half changes.
/// </summary>
public sealed class KafkaOutboxControllableExecutorFixture : ConformanceFixture
{
    string _adminConnectionString = "";
    string _databaseConnectionString = "";
    string _bootstrapServers = "";
    string _consumerGroup = "";
    NpgsqlDataSource _dataSource = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public override IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public override IGrainFactory GrainFactory => Cluster.GrainFactory;

    public string DeadLetterTableName { get; } = "edict_dead_letter";

    public string ClaimCheckTableName { get; } = "edict_claim_check";

    public override IEdictTableRepository<T> GetTableRepository<T>(string tableName) =>
        new PostgresTableRepository<T>(
            _dataSource,
            tableName,
            Cluster.Client.ServiceProvider.GetRequiredService<Serializer>());

    public override IEdictTableStoreFactory TableStoreFactory =>
        new PostgresTableWriteStoreFactory(
            _dataSource,
            Cluster.Client.ServiceProvider.GetRequiredService<Serializer>());

    public override async Task InitializeAsync()
    {
        _adminConnectionString = await PostgresAssemblyHost.GetAdminConnectionStringAsync();
        _bootstrapServers = await KafkaAssemblyHost.GetBootstrapServersAsync();
        _consumerGroup = $"edict-kafka-dl-{Guid.NewGuid():N}";

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

    public override async Task DisposeAsync()
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
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(OrderCommandHandler).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddAssembly(typeof(EdictKafkaWireEnvelope).Assembly)
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
            siloBuilder.Services.AddSingleton(TimeProvider.System);
            siloBuilder.AddEdict(o =>
            {
                o.OutboxMaxAttempts = 2;
                o.OutboxBaseDelay = TimeSpan.FromMilliseconds(200);
                o.OutboxJitterFraction = 0;
            });
            // Replace, not append — see ControllableExecutor recurring trap
            // (controllable-executor-duplicate-registration memory).
            var publish = siloBuilder.Services.Single(d =>
                d.ServiceType == typeof(IOutboxEffectExecutor)
                && d.ImplementationType == typeof(PublishEventExecutor));
            siloBuilder.Services.Remove(publish);
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, ControllableOutboxExecutor>();
            siloBuilder.AddEdictKafkaStreams(o =>
            {
                o.BootstrapServers = ctx.KafkaBootstrapServers;
                o.ConsumerGroupId = ctx.KafkaConsumerGroup;
                o.PartitionCount = 4;
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
            Microsoft.Extensions.Configuration.IConfiguration configuration,
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

[CollectionDefinition(Name)]
public sealed class KafkaOutboxControllableExecutorCollection
    : ICollectionFixture<KafkaOutboxControllableExecutorFixture>
{
    public const string Name = "KafkaOutboxControllableExecutor";
}
