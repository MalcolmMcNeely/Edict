using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure.TableStorage;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Azure.Tests;

/// <summary>
/// Full-stack Azurite-backed cluster fixture for the Azure provider test suite
///. Uses the assembly-shared Azurite container
/// (<see cref="AzuriteAssemblyHost"/>) and per-fixture Guid-prefixed resource
/// names so two collections running in parallel against the same Azurite never
/// collide. The previous shape held connection-string and service-client state
/// in <c>static</c> fields with xUnit's default cross-collection parallelism
/// active — a latent race that this rewrite removes.
/// </summary>
public sealed class AzureClusterFixture : IAsyncLifetime
{
    string _connectionString = "";
    TableServiceClient _tableServiceClient = null!;
    BlobServiceClient _blobServiceClient = null!;
    QueueServiceClient _queueServiceClient = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public TableServiceClient TableServiceClient => _tableServiceClient;

    public BlobServiceClient BlobServiceClient => _blobServiceClient;

    public QueueServiceClient QueueServiceClient => _queueServiceClient;

    /// <summary>
    /// Per-fixture Guid-prefixed blob container name backing the Orleans
    /// <c>edict-state</c> grain storage provider. The provider <i>name</i>
    /// stays <c>edict-state</c> (it is hardcoded in
    /// <c>[PersistentState("state", "edict-state")]</c> consumer attributes
    /// in tests); only the underlying blob container is per-fixture-unique
    /// so collections running in parallel against the shared Azurite do not
    /// collide on container existence or contents.
    /// </summary>
    public string GrainStateContainerName { get; private set; } = "";

    /// <summary>
    /// Per-fixture Guid-prefixed Azure Table name used by the dead-letter
    /// projection and repository for this collection.
    /// </summary>
    public string DeadLetterTableName { get; private set; } = "";

    /// <summary>
    /// Mint a fresh Guid-prefixed resource-name bundle for a single test that
    /// needs to create ad-hoc Azurite resources (per-test tables, claim-check
    /// containers, etc.). Reused across test files so the prefixing pattern
    /// is uniform.
    /// </summary>
    public AzuriteResourceNames NewResourceNames() => AzuriteResourceNames.Generate("acf");

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
            siloBuilder.Services.AddSingleton<IValidator<AzureValidateSkuCommand>, AzureSkuRequiredValidator>();
            siloBuilder.Services.AddSingleton<IValidator<AzureStateCheckCommand>, AzureGrainStateRequiredValidator>();
            siloBuilder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(_ =>
                new AzureTableRepository<EdictDeadLetterEntry>(
                    ctx.TableServiceClient, ctx.DeadLetterTableName));
            // The fixture wires its own custom Azure streams/storage (with
            // shorter visibility timeout + in-memory PubSubStore for test
            // isolation) so it registers the wiring markers manually; the
            // EdictAzureSiloBuilderExtensionsTests cover the production
            // AddEdictAzureStreams + AddEdictAzurePersistence paths.
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
            siloBuilder.AddEdict();
            siloBuilder.UseInMemoryReminderService();
            // PubSubStore stays on memory storage in this fixture — Orleans's
            // internal pub-sub state is out of scope for the Edict substrate
            // story (production keeps PubSubStore on Tables, but the
            // provider-conformance suite isolates the change under test).
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            // edict-state on Azure Blob. Substrate-behaviour tests
            // (including GrainStateOnBlobSubstrateAtomicityTests) exercise the
            // same provider the sample silo wires in production. The container
            // name is per-fixture (Guid-prefixed) so parallel collections do
            // not share state.
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
                    // Short visibility timeout lets the at-least-once redelivery
                    // test observe a real queue re-queue within seconds.
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
            var key = configuration[AzureClusterContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException(
                    "ClusterContextKey missing from client configuration.");
            var ctx = AzureClusterContextRegistry.Get(key);

            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddSingleton(ctx.TableServiceClient);
            clientBuilder.Services.AddEdict();
            clientBuilder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(_ =>
                new AzureTableRepository<EdictDeadLetterEntry>(
                    ctx.TableServiceClient, ctx.DeadLetterTableName));
        }
    }
}

[CollectionDefinition(Name)]
public sealed class AzureClusterCollection : ICollectionFixture<AzureClusterFixture>
{
    public const string Name = "AzureCluster";
}
