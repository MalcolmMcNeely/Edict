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
using Edict.Tests.Conformance.Outbox;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Kafka.Tests.DeadLetter;

/// <summary>
/// Conformance fixture wiring Kafka streams + Azure persistence with the
/// process-wide flippable <see cref="ControllableOutboxExecutor"/> in place
/// of the framework's default <see cref="PublishEventExecutor"/>. Drives the
/// dead-letter promotion path on the Kafka × Azure cross: the executor
/// exhausts its 2 attempts, the projection then writes the
/// <c>EdictDeadLetterEntry</c> row through the healed publisher. Mirrors
/// <c>KafkaOutboxControllableExecutorFixture</c>; only the persistence half
/// changes.
/// </summary>
public sealed class KafkaAzureOutboxControllableExecutorFixture : ConformanceFixture
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

    public override async Task InitializeAsync()
    {
        _azureConnectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        _bootstrapServers = await KafkaAssemblyHost.GetBootstrapServersAsync();
        _consumerGroup = $"edict-kafka-az-dl-{Guid.NewGuid():N}";

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
            siloBuilder.Services.AddSingleton(TimeProvider.System);
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(
                _ => new AzureTableWriteStoreFactory(ctx.TableServiceClient));
            siloBuilder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(_ =>
                new AzureTableRepository<EdictDeadLetterEntry>(
                    ctx.TableServiceClient, ctx.DeadLetterTableName));
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(ctx.ClaimCheckStore);
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
            siloBuilder.AddEdict(o =>
            {
                o.OutboxMaxAttempts = 2;
                o.OutboxBaseDelay = TimeSpan.FromMilliseconds(200);
                o.OutboxJitterFraction = 0;
            });
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddAzureBlobGrainStorage("edict-state", opt =>
            {
                opt.BlobServiceClient = ctx.BlobServiceClient;
                opt.ContainerName = ctx.GrainStateContainerName;
            });
            // Replace, not append — see controllable-executor-duplicate-registration
            // memory. Append would double-dispatch every effect.
            var publish = siloBuilder.Services.Single(d =>
                d.ServiceType == typeof(IOutboxEffectExecutor)
                && d.ImplementationType == typeof(PublishEventExecutor));
            siloBuilder.Services.Remove(publish);
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, ControllableOutboxExecutor>();
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
public sealed class KafkaAzureOutboxControllableExecutorCollection
    : ICollectionFixture<KafkaAzureOutboxControllableExecutorFixture>
{
    public const string Name = "KafkaAzureOutboxControllableExecutor";
}
