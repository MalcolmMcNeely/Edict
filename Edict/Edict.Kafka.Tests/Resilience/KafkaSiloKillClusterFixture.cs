using Confluent.Kafka;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.Sending;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Kafka.Internal;
using Edict.Tests.Conformance.Streaming.References;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;

using Testcontainers.Kafka;

using Xunit;

namespace Edict.Kafka.Tests.Resilience;

// Owns its own Kafka container: KillSiloAsync + RestartSiloAsync is a brittle
// dance against any cluster fixture sharing the broker. Single-silo with
// InitialSilosCount = 1 mirrors the Kafka adapter's known-working shape;
// multi-silo correctness stays out of this test surface. PartitionCountByStream
// pins the silo-kill streams to one partition so restart is a straight re-Assign
// on the same partition. Persistence is the dumb in-memory reference, shared
// across the in-process silo so the projection row the proof reads back survives
// the kill (it is a plain dictionary, not a grain).
public sealed class KafkaSiloKillClusterFixture : IAsyncLifetime
{
    KafkaContainer _kafka = null!;
    string _bootstrapServers = "";
    string _consumerGroup = "";
    ReferenceTableStoreFactory _tableStoreFactory = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public async Task InitializeAsync()
    {
        _kafka = new KafkaBuilder("confluentinc/cp-kafka:7.5.12").Build();
        await _kafka.StartAsync();

        var address = _kafka.GetBootstrapAddress();
        _bootstrapServers = address.StartsWith("PLAINTEXT://", StringComparison.Ordinal)
            ? address.Substring("PLAINTEXT://".Length)
            : address;
        _consumerGroup = $"edict-kafka-silokill-{Guid.NewGuid():N}";
        _tableStoreFactory = new ReferenceTableStoreFactory();

        var context = new KafkaResilienceContext(
            _bootstrapServers, _consumerGroup, _tableStoreFactory, new ReferenceClaimCheckStore());
        _contextKey = KafkaResilienceContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Properties[KafkaResilienceContextRegistry.ContextKeyProperty] = _contextKey;
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
        KafkaResilienceContextRegistry.Unregister(_contextKey);
        if (_kafka is not null)
        {
            await _kafka.DisposeAsync();
        }
    }

    // Reads a projection row back from the shared reference table store — the one
    // row the silo-kill proofs assert. A streaming property (the row settles under
    // redelivery) read off the reference persistence the issue scopes it to.
    public async Task<T?> GetProjectionRowAsync<T>(string tableName, Guid aggregateId)
        where T : class, new()
    {
        var store = await _tableStoreFactory.CreateAsync<T>(tableName);
        return await store.GetAsync(aggregateId.ToString("N"), aggregateId.ToString());
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(KafkaSiloKillEvent).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddAssembly(typeof(KafkaWireEnvelope).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[KafkaResilienceContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing from silo configuration.");
            var ctx = KafkaResilienceContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Configure<SiloMessagingOptions>(o => o.ResponseTimeout = TimeSpan.FromMinutes(2));
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(ctx.TableStoreFactory);
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(ctx.ClaimCheckStore);
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
            siloBuilder.AddEdict();
            siloBuilder.AddEdictKafkaStreams(o =>
            {
                o.BootstrapServers = ctx.BootstrapServers;
                o.ConsumerGroupId = ctx.ConsumerGroup;
                o.PartitionCount = 4;
                // Single-partition silo-kill streams remove all queue-balancer
                // ambiguity: one QueueId per stream, deterministic re-Assign on
                // restart.
                o.PartitionCountByStream[KafkaSiloKillEvent.StreamName] = 1;
                o.PartitionCountByStream[KafkaSiloKillBatchEvent.StreamName] = 1;
                // Earliest so the new consumer on the restarted silo replays
                // anything written before its receiver finishes Assign().
                o.AutoOffsetReset = AutoOffsetReset.Earliest;
            });
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryGrainStorage("edict-state");
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class KafkaSiloKillCollection : ICollectionFixture<KafkaSiloKillClusterFixture>
{
    public const string Name = "KafkaSiloKill";
}
