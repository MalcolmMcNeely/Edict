using Confluent.Kafka;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.Sending;
using Edict.Core;
using Edict.Core.ClaimCheck;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Kafka.Internal;
using Edict.Tests.Conformance;
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
/// The Kafka binding of the streaming battery's claim-check (pointer-branch)
/// scenarios. Identical to <see cref="KafkaStreamingFixture"/> — real Kafka
/// stream, reference persistence — except the <c>ClaimCheckPolicy</c> threshold
/// is 1 byte, so every raised event spills to the pointer branch and the body
/// lands in the reference claim-check store. Kafka registers no provider-side
/// <c>ClaimCheckPolicy</c>; the policy is read from the Core seam, so the
/// fixture-supplied one routes every publish through the store. A separate
/// fixture (and collection) keeps the non-spilling scenarios on the default
/// threshold where they assert the inline payload on the wire.
/// </summary>
public sealed class KafkaClaimCheckStreamingFixture : StreamingConformanceFixture
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
            $"edict-kafka-claimcheck-streaming-{Guid.NewGuid():N}",
            _claimCheckStore,
            new ReferenceTableStoreFactory(),
            StorageFault);
        _contextKey = KafkaStreamingClusterContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
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
            siloBuilder.Configure<SiloMessagingOptions>(o => o.ResponseTimeout = TimeSpan.FromMinutes(2));
            siloBuilder.Services.AddSingleton(TimeProvider.System);
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(ctx.TableStoreFactory);
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(ctx.ClaimCheckStore);

            // 1-byte threshold forces every raised event onto the pointer branch,
            // exercising publish-via-blob + receiver-unwrap without inflating
            // payload size. Kafka reads this Core-seam policy directly.
            siloBuilder.Services.AddSingleton(serviceProvider => new ClaimCheckPolicy(
                serviceProvider.GetRequiredService<Serializer>(),
                thresholdBytes: 1,
                store: serviceProvider.GetRequiredService<IEdictClaimCheckStore>(),
                accessors: serviceProvider.GetRequiredService<IEventStreamAccessors>()));

            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
            siloBuilder.AddEdict();
            siloBuilder.AddEdictKafkaStreams(o =>
            {
                o.BootstrapServers = ctx.BootstrapServers;
                o.ConsumerGroupId = ctx.ConsumerGroup;
                o.PartitionCount = 4;
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
            clientBuilder.Configure<ClientMessagingOptions>(o => o.ResponseTimeout = TimeSpan.FromMinutes(2));
            clientBuilder.Services.AddEdict();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class KafkaClaimCheckStreamingCollection : ICollectionFixture<KafkaClaimCheckStreamingFixture>
{
    public const string Name = "KafkaClaimCheckStreaming";
}
