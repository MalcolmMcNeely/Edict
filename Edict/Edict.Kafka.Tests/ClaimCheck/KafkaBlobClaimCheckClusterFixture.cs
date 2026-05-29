using Azure.Data.Tables;
using Azure.Storage.Blobs;

using Confluent.Kafka;

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
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Kafka.Wire;
using Edict.Tests.Conformance;
using Edict.Tests.Conformance.ClaimCheck;

using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Kafka.Tests.ClaimCheck;

/// <summary>
/// Conformance fixture for the claim-check mix-and-match case
/// "Kafka streams + Blob-backed claim-check store" (US 28/29). The Azure
/// persistence wiring registers <c>AzureBlobClaimCheckStore</c> as
/// <c>IEdictClaimCheckStore</c>; the silo's <c>ClaimCheckPolicy</c> is
/// re-bound at a 1-byte threshold so the real blob store backs every
/// pointer-branch publish. Probing "blob exists" maps to the Azurite blob
/// container directly — same shape as <c>AzureClaimCheckClusterFixture</c>,
/// only the streams provider changes.
/// </summary>
public sealed class KafkaBlobClaimCheckClusterFixture : ClaimCheckFixture
{
    string _azureConnectionString = "";
    string _bootstrapServers = "";
    string _consumerGroup = "";
    string _contextKey = "";
    TableServiceClient _tableServiceClient = null!;
    BlobServiceClient _blobServiceClient = null!;

    public TestCluster Cluster { get; private set; } = null!;

    public override IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public override IGrainFactory GrainFactory => Cluster.GrainFactory;

    public string GrainStateContainerName { get; private set; } = "";

    public string DeadLetterTableName { get; private set; } = "";

    public string ClaimCheckContainerName { get; private set; } = "";

    public override IEdictTableRepository<T> GetTableRepository<T>(string tableName) =>
        new AzureTableRepository<T>(_tableServiceClient, tableName);

    public override IEdictTableStoreFactory TableStoreFactory =>
        new AzureTableWriteStoreFactory(_tableServiceClient);

    public override async Task<bool> ClaimCheckBlobExistsAsync(string key)
    {
        var container = _blobServiceClient.GetBlobContainerClient(ClaimCheckContainerName);
        var blob = container.GetBlobClient(key);
        var response = await blob.ExistsAsync();
        return response.Value;
    }

    public override async Task InitializeAsync()
    {
        _azureConnectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        _bootstrapServers = await KafkaAssemblyHost.GetBootstrapServersAsync();
        _consumerGroup = $"edict-kafka-az-cc-{Guid.NewGuid():N}";

        _tableServiceClient = new TableServiceClient(_azureConnectionString);
        _blobServiceClient = new BlobServiceClient(_azureConnectionString);

        var token = Guid.NewGuid().ToString("N");
        GrainStateContainerName = $"edict-state-{token}";
        DeadLetterTableName = $"deadletter{token}";
        ClaimCheckContainerName = $"edict-claim-check-{token}";

        var claimCheckStore = await AzureBlobClaimCheckStore.CreateAsync(
            _blobServiceClient, ClaimCheckContainerName);

        var context = new KafkaAzureClusterContext(
            _bootstrapServers,
            _consumerGroup,
            _azureConnectionString,
            _tableServiceClient,
            _blobServiceClient,
            GrainStateContainerName,
            DeadLetterTableName,
            ClaimCheckContainerName,
            claimCheckStore);
        _contextKey = KafkaAzureClusterContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Properties[KafkaAzureClusterContextRegistry.ContextKeyProperty] = _contextKey;
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
        KafkaAzureClusterContextRegistry.Unregister(_contextKey);
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(ClaimCheckCounterAggregate).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddAssembly(typeof(EdictKafkaWireEnvelope).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[KafkaAzureClusterContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException(
                    "ClusterContextKey missing from silo configuration.");
            var ctx = KafkaAzureClusterContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Services.AddSingleton(ctx.TableServiceClient);
            siloBuilder.Services.AddSingleton(ctx.BlobServiceClient);
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(
                _ => new AzureTableWriteStoreFactory(ctx.TableServiceClient));
            siloBuilder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(_ =>
                new AzureTableRepository<EdictDeadLetterEntry>(
                    ctx.TableServiceClient, ctx.DeadLetterTableName));
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(ctx.ClaimCheckStore);
            // 1-byte threshold forces every raised event onto the pointer
            // branch — exercises publish-via-blob + receiver-unwrap without
            // inflating payload size. Re-binds after the persistence-half
            // store registration so the policy resolves the real store.
            siloBuilder.Services.AddSingleton(serviceProvider => new ClaimCheckPolicy(
                serviceProvider.GetRequiredService<Serializer>(),
                thresholdBytes: 1,
                store: serviceProvider.GetRequiredService<IEdictClaimCheckStore>(),
                accessors: serviceProvider.GetRequiredService<IEventStreamAccessors>()));
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
            siloBuilder.AddEdict();
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddAzureBlobGrainStorage("edict-state", opt =>
            {
                opt.BlobServiceClient = ctx.BlobServiceClient;
                opt.ContainerName = ctx.GrainStateContainerName;
            });
            siloBuilder.AddEdictKafkaStreams(o =>
            {
                o.BootstrapServers = ctx.KafkaBootstrapServers;
                o.ConsumerGroupId = ctx.KafkaConsumerGroup;
                o.PartitionCount = 4;
                o.AutoOffsetReset = AutoOffsetReset.Earliest;
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
public sealed class KafkaBlobClaimCheckCollection : ICollectionFixture<KafkaBlobClaimCheckClusterFixture>
{
    public const string Name = "KafkaBlobClaimCheck";
}
