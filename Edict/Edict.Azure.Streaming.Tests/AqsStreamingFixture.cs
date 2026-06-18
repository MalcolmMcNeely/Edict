using Azure.Storage.Queues;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
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

namespace Edict.Azure.Streaming.Tests;

/// <summary>
/// The AQS tracer-bullet binding of the streaming conformance battery: a silo
/// with a <strong>real</strong> Azure Queue stream provider (assembly-shared
/// Azurite) and <strong>reference persistence</strong> (Orleans memory grain
/// storage + the in-memory <c>ReferenceClaimCheckStore</c> /
/// <c>ReferenceTableStoreFactory</c>). It touches no real store, introduces no
/// dependency on <c>Edict.Substrate</c>, and brings the broker up via the
/// existing <see cref="AzuriteAssemblyHost"/>. The <c>edict-state</c> memory
/// store is wrapped with <see cref="ControllableGrainStorage"/> so the
/// redeliver-after-throw scenario can fault a consumer turn out to the stream
/// layer and observe the real queue redeliver.
/// </summary>
public class AqsStreamingFixture : StreamingConformanceFixture
{
    ReferenceClaimCheckStore _claimCheckStore = null!;
    ReferenceTableStoreFactory _tableStoreFactory = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public override IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public override IGrainFactory GrainFactory => Cluster.GrainFactory;

    public override Task<bool> ClaimCheckBlobExistsAsync(Guid eventId) =>
        Task.FromResult(_claimCheckStore.Exists(eventId));

    public override IEdictTableWriteStore<T> GetTableStore<T>(string tableName) =>
        _tableStoreFactory.CreateStore<T>(tableName);

    public override async Task InitializeAsync()
    {
        var connectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        _claimCheckStore = new ReferenceClaimCheckStore();
        _tableStoreFactory = new ReferenceTableStoreFactory();

        var context = new AqsStreamingClusterContext(
            connectionString, _claimCheckStore, _tableStoreFactory, StorageFault,
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
            // First-time AQS queue provisioning under a burst of cold activations
            // can run past Orleans' default 30s response timeout.
            siloBuilder.Configure<SiloMessagingOptions>(o => o.ResponseTimeout = TimeSpan.FromMinutes(2));
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(ctx.TableStoreFactory);
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(ctx.ClaimCheckStore);
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
            // Wrap the reference grain storage so RedeliverAfterThrow can fault a
            // consumer turn; transparent (passes writes through) while the fixture's
            // switch is off, which it is for every other scenario.
            ControllableGrainStorage.Decorate(siloBuilder.Services, ctx.StorageFault, "edict-state");
            siloBuilder.AddAzureQueueStreams("edict", configure =>
            {
                configure.ConfigureAzureQueue(opt => opt.Configure(o =>
                {
                    o.QueueServiceClient = new QueueServiceClient(ctx.ConnectionString);
                    // Short timeout lets the at-least-once redelivery scenarios
                    // observe a real queue re-queue within seconds.
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
public sealed class AqsStreamingCollection : ICollectionFixture<AqsStreamingFixture>
{
    public const string Name = "AqsStreaming";
}
