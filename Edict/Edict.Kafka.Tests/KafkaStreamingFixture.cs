using Confluent.Kafka;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.Sending;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Kafka.Internal;
using Edict.Tests.Conformance;
using Edict.Tests.Conformance.Outbox;
using Edict.Tests.Conformance.Streaming;
using Edict.Tests.Conformance.Streaming.References;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Configuration;
using Orleans.Serialization;
using Orleans.TestingHost;

using Xunit;

namespace Edict.Kafka.Tests;

/// <summary>
/// The Kafka binding of the streaming conformance battery: a silo with a
/// <strong>real</strong> Edict Kafka stream provider (assembly-shared broker,
/// per-fixture consumer group) and <strong>reference persistence</strong>
/// (Orleans memory grain storage + the in-memory <c>ReferenceClaimCheckStore</c> /
/// <c>ReferenceTableStoreFactory</c>). It touches no real store, introduces no
/// dependency on <c>Edict.Substrate</c>, and brings the broker up via the
/// existing <see cref="KafkaAssemblyHost"/>. The <c>edict-state</c> memory store
/// is wrapped with <see cref="ControllableGrainStorage"/> so the
/// redeliver-after-throw scenario can fault a consumer turn and observe whether
/// the Kafka adapter — which commits the offset only in
/// <c>MessagesDeliveredAsync</c>, after <c>HandleAsync</c> returns — redelivers
/// the uncommitted event to a clean activation. A single silo matches the Kafka
/// adapter's known-working shape; multi-silo partition ownership is unimplemented.
/// </summary>
public sealed class KafkaStreamingFixture : StreamingConformanceFixture
{
    ReferenceClaimCheckStore _claimCheckStore = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public override IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public override IGrainFactory GrainFactory => Cluster.GrainFactory;

    public override Task<bool> ClaimCheckBlobExistsAsync(Guid eventId) =>
        Task.FromResult(_claimCheckStore.Exists(eventId));

    public override async Task InitializeAsync()
    {
        var bootstrapServers = await KafkaAssemblyHost.GetBootstrapServersAsync();
        _claimCheckStore = new ReferenceClaimCheckStore();

        var context = new KafkaStreamingClusterContext(
            bootstrapServers,
            $"edict-kafka-streaming-{Guid.NewGuid():N}",
            _claimCheckStore,
            new ReferenceTableStoreFactory(),
            StorageFault,
            UsesVirtualClock ? VirtualClock : TimeProvider.System);
        _contextKey = KafkaStreamingClusterContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        // Single silo matches the Kafka adapter's known-working shape: multi-silo
        // correctness on top of manual partition Assign is unimplemented, so a
        // second silo would race the consumer-group coordinator for ownership.
        builder.Options.InitialSilosCount = 1;
        builder.Properties[KafkaStreamingClusterContextRegistry.ContextKeyProperty] = _contextKey;
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
        KafkaStreamingClusterContextRegistry.Unregister(_contextKey);
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(OrderCommandHandler).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddAssembly(typeof(KafkaWireEnvelope).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[KafkaStreamingClusterContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing from silo configuration.");
            var ctx = KafkaStreamingClusterContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            // First-time Kafka topic provisioning under a burst of cold activations
            // can run past Orleans' default 30s response timeout.
            siloBuilder.Configure<SiloMessagingOptions>(o => o.ResponseTimeout = TimeSpan.FromMinutes(2));
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(ctx.TableStoreFactory);
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(ctx.ClaimCheckStore);
            // AddEdictKafkaStreams contributes the streams marker; reference
            // persistence registers no provider, so the persistence marker is
            // supplied here to satisfy the startup wiring validator.
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
            // Registered ahead of AddEdict so the host's TryAddSingleton(TimeProvider.System)
            // loses; a fixture that opted into the virtual clock drives the engine's
            // cap/backoff/reminder timing through it.
            siloBuilder.Services.AddSingleton(ctx.Clock);
            siloBuilder.AddEdict();
            siloBuilder.AddEdictKafkaStreams(o =>
            {
                o.BootstrapServers = ctx.BootstrapServers;
                o.ConsumerGroupId = ctx.ConsumerGroup;
                // Small partition count keeps topic creation fast and the
                // stream-key → partition mapping deterministic across runs.
                o.PartitionCount = 4;
                // Earliest lets the receiver replay anything the producer wrote
                // before the fresh consumer group finished joining; the
                // production default is Latest (event-driven semantics).
                o.AutoOffsetReset = AutoOffsetReset.Earliest;
            });
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryGrainStorage("edict-state");
            // Wrap the reference grain storage so RedeliverAfterThrow can fault a
            // consumer turn; transparent (passes writes through) while the fixture's
            // switch is off, which it is for every other scenario.
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

[CollectionDefinition(Name)]
public sealed class KafkaStreamingCollection : ICollectionFixture<KafkaStreamingFixture>
{
    public const string Name = "KafkaStreaming";
}
