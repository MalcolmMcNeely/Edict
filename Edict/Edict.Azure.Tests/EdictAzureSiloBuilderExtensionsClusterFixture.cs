using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Contracts.Sending;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Azure.Tests;

public sealed class EdictAzureSiloBuilderExtensionsClusterFixture : IAsyncLifetime
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

    public string GrainStateContainerName { get; private set; } = "";

    public string DeadLetterTableName { get; private set; } = "";

    public string ClaimCheckContainerName { get; private set; } = "";

    public AzuriteResourceNames NewResourceNames() => AzuriteResourceNames.Generate("eabx");

    public async Task InitializeAsync()
    {
        _connectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        _tableServiceClient = new TableServiceClient(_connectionString);
        _blobServiceClient = new BlobServiceClient(_connectionString);
        _queueServiceClient = new QueueServiceClient(_connectionString);

        var token = Guid.NewGuid().ToString("N");
        GrainStateContainerName = $"edict-state-{token}";
        DeadLetterTableName = $"edictdeadletter{token}";
        ClaimCheckContainerName = $"edict-claim-check-{token}";

        var context = new AzureClusterContext(
            _connectionString,
            _tableServiceClient,
            _blobServiceClient,
            _queueServiceClient,
            GrainStateContainerName,
            DeadLetterTableName)
        { };
        _contextKey = AzureClusterContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Properties[AzureClusterContextRegistry.ContextKeyProperty] = _contextKey;
        // Carries the claim-check container separately so the configurator
        // doesn't widen the shared context shape.
        builder.Properties[ClaimCheckContainerProperty] = ClaimCheckContainerName;
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

    const string ClaimCheckContainerProperty = "Edict.Azure.Tests.ClaimCheckContainer";

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
            var claimCheckContainer = siloBuilder.Configuration[ClaimCheckContainerProperty]
                ?? throw new InvalidOperationException(
                    "ClaimCheckContainer missing from silo configuration.");

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            // Azurite first-time table creation can run past Orleans' default
            // 30s response timeout when storage + reminder tables provision
            // in parallel; the bumped timeout gives the silo headroom for
            // the first command roundtrip.
            siloBuilder.Configure<global::Orleans.Configuration.SiloMessagingOptions>(o =>
            {
                o.ResponseTimeout = TimeSpan.FromMinutes(2);
            });

            // Memory reminders + PubSub keep the test focused on the
            // Edict-specific Azurite substrate (streams, grain-state blob,
            // dead-letter table) — Orleans' own Azure Table reminder/pubsub
            // path is already covered by AzureClusterFixture.
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");

            siloBuilder.AddEdict();
            siloBuilder.AddEdictAzureStreams(o =>
            {
                o.QueueServiceClient = ctx.QueueServiceClient;
            });
            siloBuilder.AddEdictAzurePersistence(o =>
            {
                o.TableServiceClient = ctx.TableServiceClient;
                o.BlobServiceClient = ctx.BlobServiceClient;
                o.GrainStateContainerName = ctx.GrainStateContainerName;
                o.DeadLetterTableName = ctx.DeadLetterTableName;
                o.ClaimCheckBlobContainerName = claimCheckContainer;
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
            clientBuilder.Configure<global::Orleans.Configuration.ClientMessagingOptions>(o =>
            {
                o.ResponseTimeout = TimeSpan.FromMinutes(2);
            });
        }
    }
}

[CollectionDefinition(Name)]
public sealed class EdictAzureSiloBuilderExtensionsClusterCollection
    : ICollectionFixture<EdictAzureSiloBuilderExtensionsClusterFixture>
{
    public const string Name = "EdictAzureSiloBuilderExtensionsCluster";
}
