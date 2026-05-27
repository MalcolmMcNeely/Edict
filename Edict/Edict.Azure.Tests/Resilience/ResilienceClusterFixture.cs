using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Docker.DotNet;

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
using Edict.Tests.Conformance;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.TestingHost;

using Testcontainers.Azurite;

namespace Edict.Azure.Tests.Resilience;

// Owns its own Azurite container instead of sharing AzuriteAssemblyHost:
// pausing or restarting mid-test would corrupt every parallel collection
// running against the shared instance.
public sealed class ResilienceClusterFixture : IAsyncLifetime
{
    AzuriteContainer _azurite = null!;
    DockerClient _dockerClient = null!;
    string _connectionString = "";
    TableServiceClient _tableServiceClient = null!;
    BlobServiceClient _blobServiceClient = null!;
    QueueServiceClient _queueServiceClient = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public string GrainStateContainerName { get; private set; } = "";

    public string DeadLetterTableName { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _azurite = new AzuriteBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:3.35.0")
            .WithCreateParameterModifier(p =>
            {
                p.Cmd ??= [];
                p.Cmd.Add("--skipApiVersionCheck");
            })
            .Build();
        await _azurite.StartAsync();
        _connectionString = _azurite.GetConnectionString();
        _dockerClient = new DockerClientConfiguration().CreateClient();

        RebuildClients();

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
        _dockerClient?.Dispose();
        if (_azurite is not null)
        {
            await _azurite.DisposeAsync();
        }
    }

    // Pause preserves the host port binding; Testcontainers 3.10 has no
    // PauseAsync on IContainer, so the calls go through Docker.DotNet.
    public async Task PauseAzuriteAsync() =>
        await _dockerClient.Containers.PauseContainerAsync(_azurite.Id);

    public async Task UnpauseAzuriteAsync() =>
        await _dockerClient.Containers.UnpauseContainerAsync(_azurite.Id);

    // Stop+start re-binds the host port — already-active grains keep a stale
    // client, so each test must use a fresh grain key.
    public async Task RestartAzuriteAsync()
    {
        await _azurite.StopAsync();
        await _azurite.StartAsync();
        RefreshConnectionState();
    }

    // Tests call this on entry so the fixture starts from a known-good
    // baseline even if a previous test panicked mid pause/restart.
    public async Task EnsureRunningAsync()
    {
        if (_azurite.State != DotNet.Testcontainers.Containers.TestcontainersStates.Running)
        {
            await _azurite.StartAsync();
            RefreshConnectionState();
            return;
        }

        try
        {
            await _dockerClient.Containers.UnpauseContainerAsync(_azurite.Id);
        }
        catch (DockerApiException)
        {
            // Container was not paused — nothing to do.
        }
    }

    void RefreshConnectionState()
    {
        // Azurite re-binds to a new ephemeral port on restart, so clients and
        // the registry entry must be refreshed before silo configurators
        // capture context at the next grain activation.
        _connectionString = _azurite.GetConnectionString();
        RebuildClients();
        var refreshed = new AzureClusterContext(
            _connectionString,
            _tableServiceClient,
            _blobServiceClient,
            _queueServiceClient,
            GrainStateContainerName,
            DeadLetterTableName);
        AzureClusterContextRegistry.Replace(_contextKey, refreshed);
    }

    void RebuildClients()
    {
        _tableServiceClient = new TableServiceClient(_connectionString);
        _blobServiceClient = new BlobServiceClient(_connectionString);
        _queueServiceClient = new QueueServiceClient(_connectionString);
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
            siloBuilder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(_ =>
                new AzureTableRepository<EdictDeadLetterEntry>(
                    ctx.TableServiceClient, ctx.DeadLetterTableName));
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
                    // Short visibility timeout so a paused/restarted Azurite
                    // produces an observable redelivery within the test budget.
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

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ResilienceCollection : ICollectionFixture<ResilienceClusterFixture>
{
    public const string Name = "Resilience";
}
