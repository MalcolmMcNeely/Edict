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
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Kafka.Wire;
using Edict.Tests.Conformance;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Kafka.Tests;

/// <summary>
/// Conformance fixture binding <see cref="ConformanceFixture"/> to a
/// Kafka-streams × Azure-persistence substrate cross. Kafka shares the
/// assembly-scoped broker container (own consumer-group per fixture so
/// offsets don't collide); Azurite is shared too but every container/table
/// name is per-fixture-namespaced. Streams half goes through
/// <see cref="EdictKafkaSiloBuilderExtensions.AddEdictKafkaStreams"/>;
/// persistence half through
/// <see cref="EdictAzureSiloBuilderExtensions.AddEdictAzurePersistence"/> —
/// proves the same conformance battery from the Postgres pairing rides on
/// the Kafka × Azure mix-and-match shape end-to-end.
/// </summary>
public sealed class KafkaAzureClusterFixture : ConformanceFixture
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

    public TableServiceClient TableServiceClient => _tableServiceClient;

    public BlobServiceClient BlobServiceClient => _blobServiceClient;

    public string BootstrapServers => _bootstrapServers;

    public string ConsumerGroup => _consumerGroup;

    public string GrainStateContainerName { get; private set; } = "";

    public string DeadLetterTableName { get; private set; } = "";

    public string ClaimCheckContainerName { get; private set; } = "";

    public override IEdictTableRepository<T> GetTableRepository<T>(string tableName) =>
        new AzureTableRepository<T>(_tableServiceClient, tableName);

    public override IEdictTableStoreFactory TableStoreFactory =>
        new AzureTableWriteStoreFactory(_tableServiceClient);

    public override async Task InitializeAsync()
    {
        _azureConnectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        _bootstrapServers = await KafkaAssemblyHost.GetBootstrapServersAsync();
        _consumerGroup = $"edict-kafka-az-{Guid.NewGuid():N}";

        _tableServiceClient = new TableServiceClient(_azureConnectionString);
        _blobServiceClient = new BlobServiceClient(_azureConnectionString);

        var token = Guid.NewGuid().ToString("N");
        GrainStateContainerName = $"edict-state-{token}";
        DeadLetterTableName = $"deadletter{token}";
        ClaimCheckContainerName = $"edict-claim-check-{token}";

        // Build the claim-check store eagerly on the host thread — a
        // .GetAwaiter().GetResult() registration deadlocks on the grain task
        // scheduler at first activation (see azure-blob-claim-check-grain-thread-hang memory).
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
            .AddAssembly(typeof(OrderCommandHandler).Assembly)
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
            siloBuilder.Services.AddSingleton<IValidator<ValidateSkuCommand>, SkuRequiredValidator>();
            siloBuilder.Services.AddSingleton<IValidator<StateCheckCommand>, GrainStateRequiredValidator>();
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(
                _ => new AzureTableWriteStoreFactory(ctx.TableServiceClient));
            siloBuilder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(_ =>
                new AzureTableRepository<EdictDeadLetterEntry>(
                    ctx.TableServiceClient, ctx.DeadLetterTableName));
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(ctx.ClaimCheckStore);
            // Wire the persistence/streams markers manually — the fixture
            // bypasses AddEdictAzurePersistence so it can pin the provider
            // name to "edict-state" (consumer attribute lookup) while keeping
            // the underlying container per-fixture for collection isolation.
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
                // Earliest matches the Kafka × Postgres fixture rationale —
                // the test producer can write before the fresh consumer group
                // finishes joining; production default is Latest (ADR-0001).
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
            var key = configuration[KafkaAzureClusterContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException(
                    "ClusterContextKey missing from client configuration.");
            var ctx = KafkaAzureClusterContextRegistry.Get(key);

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
public sealed class KafkaAzureClusterCollection : ICollectionFixture<KafkaAzureClusterFixture>
{
    public const string Name = "KafkaAzureCluster";
}
