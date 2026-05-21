using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure.ClaimCheck;
using Edict.Azure.TableStorage;
using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.ClaimCheck;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Azure.Tests.ClaimCheck;

/// <summary>
/// Azurite-backed cluster for the lifted ClaimCheck publish + receiver-unwrap
/// scenarios (issue #90). Wires a real
/// <see cref="AzureBlobClaimCheckStore"/> against the shared Azurite blob
/// service and overrides <see cref="ClaimCheckPolicy"/> with a 1-byte
/// threshold so every raised event takes the pointer branch — the publish
/// path always uploads to blob and the receiver always unwraps from blob.
/// Uses the assembly-scoped Azurite host (<see cref="AzuriteAssemblyHost"/>)
/// with per-fixture Guid-prefixed resource names so it does not
/// collide with the other collections sharing the same Azurite.
/// </summary>
public sealed class AzureClaimCheckClusterFixture : IAsyncLifetime
{
    string _connectionString = "";
    TableServiceClient _tableServiceClient = null!;
    BlobServiceClient _blobServiceClient = null!;
    QueueServiceClient _queueServiceClient = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public BlobServiceClient BlobServiceClient => _blobServiceClient;

    public string ClaimCheckContainerName { get; private set; } = "";

    public string GrainStateContainerName { get; private set; } = "";

    public string DeadLetterTableName { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _connectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        _tableServiceClient = new TableServiceClient(_connectionString);
        _blobServiceClient = new BlobServiceClient(_connectionString);
        _queueServiceClient = new QueueServiceClient(_connectionString);

        var token = Guid.NewGuid().ToString("N");
        GrainStateContainerName = $"edict-state-{token}";
        DeadLetterTableName = $"deadletter{token}";
        ClaimCheckContainerName = $"edict-claim-check-{token}";

        // Create the AzureBlobClaimCheckStore eagerly here (async-safe) so the
        // silo registration can hand back the instance without a sync-over-async
        // .GetAwaiter().GetResult() on the grain task scheduler — that path
        // hung the first run of these tests on container creation.
        var claimCheckStore = await AzureBlobClaimCheckStore.CreateAsync(
            _blobServiceClient, ClaimCheckContainerName);

        var context = new AzureClusterContext(
            _connectionString,
            _tableServiceClient,
            _blobServiceClient,
            _queueServiceClient,
            GrainStateContainerName,
            DeadLetterTableName,
            ClaimCheckContainerName,
            claimCheckStore);
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
            .AddAssembly(typeof(AzureClaimCheckCounterAggregate).Assembly)
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
            siloBuilder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(_ =>
                new AzureTableRepository<EdictDeadLetterEntry>(
                    ctx.TableServiceClient, ctx.DeadLetterTableName));
            siloBuilder.Services.AddSingleton(TimeProvider.System);

            // Real Azure Blob claim-check store, against the shared Azurite
            // with a per-fixture container. The instance was created eagerly
            // in the fixture's InitializeAsync — registering it here as an
            // instance avoids running the async container-creation on the
            // grain task scheduler during first resolution.
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(ctx.ClaimCheckStore!);

            // Override the default ClaimCheckPolicy (threshold=int.MaxValue,
            // registered via TryAddSingleton in AddEdictOutbox) with a 1-byte
            // threshold so every raised event takes the pointer branch and we
            // can observe the publish-via-blob + receiver-unwrap paths without
            // having to inflate payload size.
            siloBuilder.Services.AddSingleton(sp => new ClaimCheckPolicy(
                sp.GetRequiredService<Serializer>(),
                thresholdBytes: 1,
                store: sp.GetRequiredService<IEdictClaimCheckStore>()));

            // Custom Azure wiring (test-only stream + storage shape), so the
            // fixture registers the wiring markers manually rather than
            // calling AddEdictAzureStreams/Persistence.
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
            siloBuilder.AddEdict();
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
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
public sealed class AzureClaimCheckCollection : ICollectionFixture<AzureClaimCheckClusterFixture>
{
    public const string Name = "AzureClaimCheck";
}
