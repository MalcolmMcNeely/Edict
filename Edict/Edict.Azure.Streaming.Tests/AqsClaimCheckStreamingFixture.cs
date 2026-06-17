using Azure.Storage.Queues;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.Sending;
using Edict.Core;
using Edict.Core.ClaimCheck;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
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

namespace Edict.Azure.Streaming.Tests;

/// <summary>
/// The AQS binding of the streaming battery's claim-check (pointer-branch)
/// scenarios. Identical to <see cref="AqsStreamingFixture"/> — real AQS stream,
/// reference persistence — except the <c>ClaimCheckPolicy</c> threshold is
/// 1 byte, so every raised event spills to the pointer branch and the body lands
/// in the reference claim-check store. A separate fixture (and collection) keeps
/// the non-spilling scenarios on the default threshold where they assert the
/// inline payload on the wire.
/// </summary>
public sealed class AqsClaimCheckStreamingFixture : StreamingConformanceFixture
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
        var connectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        _claimCheckStore = new ReferenceClaimCheckStore();

        var context = new AqsStreamingClusterContext(
            connectionString, _claimCheckStore, new ReferenceTableStoreFactory(), StorageFault,
            UsesVirtualClock ? VirtualClock : TimeProvider.System);
        _contextKey = AqsStreamingClusterContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Properties[AqsStreamingClusterContextRegistry.ContextKeyProperty] = _contextKey;
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
        AqsStreamingClusterContextRegistry.Unregister(_contextKey);
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(OrderCommandHandler).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[AqsStreamingClusterContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing from silo configuration.");
            var ctx = AqsStreamingClusterContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Configure<SiloMessagingOptions>(o => o.ResponseTimeout = TimeSpan.FromMinutes(2));
            siloBuilder.Services.AddSingleton(TimeProvider.System);
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(ctx.TableStoreFactory);
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(ctx.ClaimCheckStore);

            // 1-byte threshold forces every raised event onto the pointer branch,
            // exercising publish-via-blob + receiver-unwrap without inflating
            // payload size.
            siloBuilder.Services.AddSingleton(serviceProvider => new ClaimCheckPolicy(
                serviceProvider.GetRequiredService<Serializer>(),
                thresholdBytes: 1,
                store: serviceProvider.GetRequiredService<IEdictClaimCheckStore>(),
                accessors: serviceProvider.GetRequiredService<IEventStreamAccessors>()));

            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
            // Registered ahead of AddEdict so the host's TryAddSingleton(TimeProvider.System)
            // loses; a fixture that opted into the virtual clock drives the engine's
            // cap/backoff/reminder timing through it.
            siloBuilder.Services.AddSingleton(ctx.Clock);
            siloBuilder.AddEdict();
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryGrainStorage("edict-state");
            siloBuilder.AddAzureQueueStreams("edict", configure =>
            {
                configure.ConfigureAzureQueue(opt => opt.Configure(o =>
                {
                    o.QueueServiceClient = new QueueServiceClient(ctx.ConnectionString);
                    o.MessageVisibilityTimeout = TimeSpan.FromSeconds(5);
                }));
                configure.ConfigurePullingAgent(opt => opt.Configure(o =>
                    o.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(200)));
            });
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
public sealed class AqsClaimCheckStreamingCollection : ICollectionFixture<AqsClaimCheckStreamingFixture>
{
    public const string Name = "AqsClaimCheckStreaming";
}
