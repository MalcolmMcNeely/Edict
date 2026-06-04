using Confluent.Kafka;

using Edict.Contracts.Sending;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Kafka;
using Edict.Postgres;
using Edict.Tests.Conformance.Outbox;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Configuration;
using Orleans.Serialization;
using Orleans.TestingHost;

using Xunit;

namespace Edict.Pairing.Tests;

/// <summary>
/// The Kafka+Postgres shipped pairing: a single silo wired through the documented
/// consumer stack — <c>AddEdictKafkaStreams</c> + <c>AddEdictPostgresPersistence</c>
/// — over the assembly-shared Kafka broker (per-fixture consumer group) and a
/// per-fixture Postgres database, with the real Postgres <c>edict-state</c> store
/// wrapped in <see cref="ControllableGrainStorage"/>. Boot proves the documented
/// composition with both real providers' types present stands up and round-trips;
/// the conjunction faults the real Postgres write and lets the real Kafka stream
/// redeliver (the adapter commits the offset only after <c>HandleAsync</c>).
/// </summary>
public sealed class KafkaPostgresPairingFixture : PairingFixture
{
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public override IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public override IGrainFactory GrainFactory => Cluster.GrainFactory;

    public override async Task InitializeAsync()
    {
        var bootstrapServers = await KafkaAssemblyHost.GetBootstrapServersAsync();
        var adminConnectionString = await PostgresAssemblyHost.GetAdminConnectionStringAsync();
        var databaseName = $"edict_{Guid.NewGuid():N}";
        var connectionString = await PostgresDatabaseFactory.CreateDatabaseAsync(adminConnectionString, databaseName);

        var context = new KafkaPostgresPairingContext(
            bootstrapServers,
            ConsumerGroup: $"edict-pairing-{Guid.NewGuid():N}",
            ConnectionString: connectionString,
            StorageFault: StorageFault);
        _contextKey = PairingContextRegistry<KafkaPostgresPairingContext>.Register(context);

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Properties[PairingContextRegistry<KafkaPostgresPairingContext>.ContextKeyProperty] = _contextKey;
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
        PairingContextRegistry<KafkaPostgresPairingContext>.Unregister(_contextKey);
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(CounterAggregate).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddAssembly(typeof(EdictKafkaStreamsOptions).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[PairingContextRegistry<KafkaPostgresPairingContext>.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing from silo configuration.");
            var ctx = PairingContextRegistry<KafkaPostgresPairingContext>.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Configure<SiloMessagingOptions>(o => o.ResponseTimeout = TimeSpan.FromMinutes(2));

            siloBuilder.AddEdict();
            siloBuilder.AddEdictKafkaStreams(o =>
            {
                o.BootstrapServers = ctx.BootstrapServers;
                o.ConsumerGroupId = ctx.ConsumerGroup;
                o.PartitionCount = 4;
                // Earliest replays anything written before the fresh consumer group
                // finished joining, and lets the conjunction's redelivered (never
                // committed) event reach the clean activation.
                o.AutoOffsetReset = AutoOffsetReset.Earliest;
            });
            siloBuilder.AddEdictPostgresPersistence(o =>
            {
                o.ConnectionString = ctx.ConnectionString;
            });
            // Wrap the real Postgres edict-state store so the conjunction can fault
            // a real grain-state write; transparent while the fixture's switch is off.
            ControllableGrainStorage.Decorate(siloBuilder.Services, ctx.StorageFault, "edict-state");
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Configure<ClientMessagingOptions>(o => o.ResponseTimeout = TimeSpan.FromMinutes(2));
            clientBuilder.Services.AddEdict();
        }
    }
}

sealed record KafkaPostgresPairingContext(
    string BootstrapServers, string ConsumerGroup, string ConnectionString, StorageFaultState StorageFault);

[CollectionDefinition(Name)]
public sealed class KafkaPostgresPairingCollection : ICollectionFixture<KafkaPostgresPairingFixture>
{
    public const string Name = "KafkaPostgresPairing";
}
