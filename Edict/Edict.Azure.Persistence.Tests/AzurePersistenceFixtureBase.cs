using Azure.Data.Tables;
using Azure.Storage.Blobs;

using Edict.Azure.Persistence.TableStorage;
using Edict.Azure.Streaming.ClaimCheck;
using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.ClaimCheck;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Tests.Conformance;
using Edict.Tests.Conformance.Outbox;
using Edict.Tests.Conformance.Persistence;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Configuration;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Azure.Persistence.Tests;

/// <summary>
/// Shared bring-up for every Azure persistence-axis conformance fixture: a silo
/// with a <strong>dumb deliver-once <c>MemoryStreams</c> reference</strong>
/// behind <c>"edict"</c> and <strong>real Azure persistence</strong> over the
/// assembly-shared Azurite — Azure Blob grain storage, the Azure Table read/write
/// store, the literal dead-letter table, and a real Azure Blob claim-check store.
/// Subclasses pick the per-shape knobs (outbox options, controllable
/// publish-executor, controllable grain storage, claim-check threshold) by
/// overriding the virtual hooks; one configurator applies them. The reference
/// stream is never asserted upon — the surface only exposes the send seam, grain
/// probes, and the durable table seams, so a persistence scenario cannot assert a
/// streaming property.
/// </summary>
public abstract class AzurePersistenceFixtureBase : PersistenceConformanceFixture
{
    string _connectionString = "";
    TableServiceClient _tableServiceClient = null!;
    BlobServiceClient _blobServiceClient = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public override IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public override IGrainFactory GrainFactory => Cluster.GrainFactory;

    public override IEdictTableWriteStore<T> GetTableStore<T>(string tableName) =>
        new AzureTableWriteStore<T>(_tableServiceClient.GetTableClient(tableName));

    public override IEdictTableStoreFactory TableStoreFactory =>
        new AzureTableWriteStoreFactory(_tableServiceClient);

    protected BlobServiceClient BlobServiceClient => _blobServiceClient;

    // IEdictClaimCheckStore is internal, so this is private-protected — the
    // claim-check subclasses that read it are derived and in this assembly.
    private protected IEdictClaimCheckStore ClaimCheckStore { get; private set; } = null!;

    protected string ClaimCheckContainerName { get; private set; } = "";

    protected string GrainStateContainerName { get; private set; } = "";

    protected string DeadLetterTableName { get; private set; } = "";

    // Per-shape knobs — a subclass overrides only the ones it needs.
    protected virtual Action<EdictOptions>? ConfigureOptions => null;

    protected virtual bool ReplacePublishExecutorWithControllable => false;

    protected virtual bool DecorateGrainStorage => false;

    protected virtual int? ClaimCheckThresholdBytes => null;

    // A schedule fixture returns a FakeTimeProvider here so its scenarios can push
    // a schedule past-due on a virtual clock; every other fixture leaves it null
    // and runs on TimeProvider.System.
    protected virtual TimeProvider? ClockOverride => null;

    public override async Task InitializeAsync()
    {
        _connectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        _tableServiceClient = new TableServiceClient(_connectionString);
        _blobServiceClient = new BlobServiceClient(_connectionString);

        var token = Guid.NewGuid().ToString("N");
        GrainStateContainerName = $"edict-state-{token}";
        DeadLetterTableName = $"deadletter{token}";
        ClaimCheckContainerName = $"edict-claim-check-{token}";

        // Built eagerly off the grain task scheduler — a sync-over-async path in
        // a lazy singleton factory deadlocks first-time container creation.
        ClaimCheckStore = await AzureBlobClaimCheckStore.CreateAsync(
            _blobServiceClient, ClaimCheckContainerName);

        var context = new AzurePersistenceContext(
            _tableServiceClient,
            _blobServiceClient,
            GrainStateContainerName,
            DeadLetterTableName,
            ClaimCheckStore,
            ConfigureOptions,
            ReplacePublishExecutorWithControllable,
            DecorateGrainStorage,
            ClaimCheckThresholdBytes,
            OutboxFault,
            StorageFault,
            ClockOverride);
        _contextKey = AzurePersistenceContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Properties[AzurePersistenceContextRegistry.ContextKeyProperty] = _contextKey;
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
        AzurePersistenceContextRegistry.Unregister(_contextKey);
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(CounterAggregate).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[AzurePersistenceContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing from silo configuration.");
            var ctx = AzurePersistenceContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            // Azurite first-time blob/table provisioning under a burst of cold
            // grain activations can run past Orleans' default 30s response timeout.
            siloBuilder.Configure<SiloMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromMinutes(2));
            siloBuilder.Services.AddSingleton(ctx.TableServiceClient);
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(
                _ => new AzureTableWriteStoreFactory(ctx.TableServiceClient));
            siloBuilder.Services.AddSingleton(ctx.ClockOverride ?? TimeProvider.System);
            siloBuilder.Services.AddSingleton(ctx.ClaimCheckStore);

            if (ctx.ClaimCheckThresholdBytes is int thresholdBytes)
            {
                // A low threshold forces every raised event onto the pointer
                // branch, exercising the receiver-unwrap path on the real store.
                siloBuilder.Services.AddSingleton(serviceProvider => new ClaimCheckPolicy(
                    serviceProvider.GetRequiredService<Serializer>(),
                    thresholdBytes: thresholdBytes,
                    store: serviceProvider.GetRequiredService<IEdictClaimCheckStore>(),
                    accessors: serviceProvider.GetRequiredService<IEventStreamAccessors>()));
            }

            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();

            if (ctx.ConfigureOptions is { } configureOptions)
            {
                siloBuilder.AddEdict(configureOptions);
            }
            else
            {
                siloBuilder.AddEdict();
            }

            if (ctx.ReplacePublishExecutorWithControllable)
            {
                ControllableOutboxExecutor.Replace(siloBuilder.Services, ctx.OutboxFault);
            }

            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddAzureBlobGrainStorage("edict-state", options =>
            {
                options.BlobServiceClient = ctx.BlobServiceClient;
                options.ContainerName = ctx.GrainStateContainerName;
            });

            if (ctx.DecorateGrainStorage)
            {
                ControllableGrainStorage.Decorate(siloBuilder.Services, ctx.StorageFault);
            }

            // The dumb deliver-once reference stream: MemoryStreams delivers each
            // publish once to its implicit subscribers, carries the EventId
            // intact, and never redelivers — the persistence axis never asserts a
            // streaming property of it.
            siloBuilder.AddMemoryStreams("edict");
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Configure<ClientMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromMinutes(2));
            clientBuilder.Services.AddEdict();
        }
    }
}
