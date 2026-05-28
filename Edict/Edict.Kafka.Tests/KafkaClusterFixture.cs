using Confluent.Kafka;

using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Kafka;
using Edict.Kafka.Wire;
using Edict.Postgres;
using Edict.Postgres.TableStorage;
using Edict.Tests.Conformance;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orleans;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Kafka.Tests;

/// <summary>
/// Conformance fixture binding <see cref="ConformanceFixture"/> to a
/// Kafka-streams × Postgres-persistence substrate cross. Postgres is
/// per-fixture (own database inside the shared testcontainer); Kafka shares
/// a single broker container but each fixture mints its own consumer group so
/// offsets don't collide. The configurator invokes
/// <see cref="EdictKafkaSiloBuilderExtensions.AddEdictKafkaStreams"/> for the
/// streams half and
/// <see cref="EdictPostgresSiloBuilderExtensions.AddEdictPostgresPersistence"/>
/// for the persistence half — proves the seam compiles through the
/// framework's documented composition shape against a non-Azure pairing.
/// </summary>
public sealed class KafkaClusterFixture : ConformanceFixture
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

    public string PostgresConnectionString => _databaseConnectionString;

    public string BootstrapServers => _bootstrapServers;

    public string ConsumerGroup => _consumerGroup;

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
        _consumerGroup = $"edict-kafka-tests-{Guid.NewGuid():N}";

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
        // Slice-1 tracer: single silo matches the spike's known-working shape.
        // Multi-silo correctness on top of manual partition Assign is a #139b
        // concern — Orleans' queue balancer + Kafka's consumer-group
        // coordination have to agree on partition ownership, which the
        // adapter doesn't yet wire.
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
            siloBuilder.AddEdict();
            siloBuilder.AddEdictKafkaStreams(o =>
            {
                o.BootstrapServers = ctx.KafkaBootstrapServers;
                o.ConsumerGroupId = ctx.KafkaConsumerGroup;
                // Match the spike's tracer-bullet shape: small partition count
                // keeps topic creation fast and the stream-key → partition
                // mapping deterministic across fixture runs.
                o.PartitionCount = 4;
                // Tests pin Earliest so the receiver replays anything the
                // producer wrote before the consumer-group member finished
                // joining — the production default is Latest (event-driven
                // semantics) and the fresh-group race would otherwise drop
                // events under load.
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
            clientBuilder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(sp =>
                new PostgresTableRepository<EdictDeadLetterEntry>(
                    sp.GetRequiredService<NpgsqlDataSource>(),
                    ctx.DeadLetterTableName,
                    sp.GetRequiredService<Serializer>()));
        }
    }
}

[CollectionDefinition(Name)]
public sealed class KafkaClusterCollection : ICollectionFixture<KafkaClusterFixture>
{
    public const string Name = "KafkaCluster";
}
