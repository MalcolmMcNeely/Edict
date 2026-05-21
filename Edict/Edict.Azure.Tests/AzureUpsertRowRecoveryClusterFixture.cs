using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure.TableStorage;
using Edict.Contracts.Configuration;
using Edict.Contracts.Sending;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Azure.Tests;

/// <summary>
/// Azurite-backed cluster with a real <see cref="PublishEventExecutor"/> (so the
/// published event reaches the projection over the <b>real Azure Queue
/// stream</b>) and a flippable <see cref="AzureControllableUpsertRowExecutor"/>
/// writing to <b>real Azure Table Storage</b>. Drives the provider
/// conformance proof that's double-apply gap is closed: a crash
/// between the ring/outbox commit and the row write recovers effectively-once.
/// Shares the assembly-scoped Azurite (<see cref="AzuriteAssemblyHost"/>) and
/// uses per-fixture Guid-prefixed container/table names.
/// </summary>
public sealed class AzureUpsertRowRecoveryClusterFixture : IAsyncLifetime
{
    string _connectionString = "";
    TableServiceClient _tableServiceClient = null!;
    BlobServiceClient _blobServiceClient = null!;
    QueueServiceClient _queueServiceClient = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public TableServiceClient TableServiceClient => _tableServiceClient;

    public BlobServiceClient BlobServiceClient => _blobServiceClient;

    public QueueServiceClient QueueServiceClient => _queueServiceClient;

    public string GrainStateContainerName { get; private set; } = "";

    public string DeadLetterTableName { get; private set; } = "";

    public AzuriteResourceNames NewResourceNames() => AzuriteResourceNames.Generate("aurc");

    public async Task InitializeAsync()
    {
        _connectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        _tableServiceClient = new TableServiceClient(_connectionString);
        _blobServiceClient = new BlobServiceClient(_connectionString);
        _queueServiceClient = new QueueServiceClient(_connectionString);

        var token = Guid.NewGuid().ToString("N");
        GrainStateContainerName = $"edict-state-{token}";
        DeadLetterTableName = $"deadletter{token}";

        var context = new AzureClusterContext(
            _connectionString,
            _tableServiceClient,
            _blobServiceClient,
            _queueServiceClient,
            GrainStateContainerName,
            DeadLetterTableName);
        _contextKey = AzureClusterContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Properties[AzureClusterContextRegistry.ContextKeyProperty] = _contextKey;
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
        AzureClusterContextRegistry.Unregister(_contextKey);
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(AzureOrderCommandHandler).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[AzureClusterContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException(
                    "ClusterContextKey missing from silo configuration.");
            var ctx = AzureClusterContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Services.AddSingleton(ctx.TableServiceClient);
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(
                _ => new AzureTableWriteStoreFactory(ctx.TableServiceClient));
            siloBuilder.Services.AddSingleton(TimeProvider.System);
            // Custom Azure provider wiring (test-only AzureControllableUpsertRowExecutor),
            // so the fixture registers wiring markers manually rather than
            // calling AddEdictAzureStreams/Persistence.
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
            siloBuilder.AddEdict(o =>
            {
                // Small deterministic backoff; default MaxAttempts so the
                // failing window never dead-letters before recovery.
                o.OutboxBaseDelay = TimeSpan.FromMilliseconds(200);
                o.OutboxJitterFraction = 0;
            });
            // Replace the auto-registered UpsertRowExecutor with the controllable
            // one. The previous shape pre-registered both PublishEventExecutor and
            // AzureControllableUpsertRowExecutor before calling AddEdict(), which
            // appended duplicate IOutboxEffectExecutor entries — the OutboxHost
            // ctor's ToDictionary on OutboxEffectKind throws on the duplicate
            // PublishEvent and UpsertRow keys (see memory/upsertrow-gapclosure-azure-flake).
            var upsert = siloBuilder.Services.Single(d =>
                d.ServiceType == typeof(IOutboxEffectExecutor)
                && d.ImplementationType == typeof(UpsertRowExecutor));
            siloBuilder.Services.Remove(upsert);
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, AzureControllableUpsertRowExecutor>();
            siloBuilder.UseInMemoryReminderService();
            // PubSubStore stays on memory storage — Orleans's internal
            // pub-sub state is out of scope for the Edict substrate story.
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            // edict-state on Azure Blob — provider conformance
            // tests must exercise the same substrate the sample silo wires.
            // Container is Guid-prefixed for cross-collection isolation.
            siloBuilder.AddAzureBlobGrainStorage("edict-state", options =>
            {
                options.BlobServiceClient = ctx.BlobServiceClient;
                options.ContainerName = ctx.GrainStateContainerName;
            });
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
        public void Configure(
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            IClientBuilder clientBuilder)
        {
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class AzureUpsertRowRecoveryClusterCollection
    : ICollectionFixture<AzureUpsertRowRecoveryClusterFixture>
{
    public const string Name = "AzureUpsertRowRecoveryCluster";
}
