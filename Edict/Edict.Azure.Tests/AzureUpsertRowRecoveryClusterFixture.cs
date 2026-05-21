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

using Testcontainers.Azurite;

namespace Edict.Azure.Tests;

/// <summary>
/// Azurite-backed cluster with a real <see cref="PublishEventExecutor"/> (so the
/// published event reaches the projection over the <b>real Azure Queue
/// stream</b>) and a flippable <see cref="AzureControllableUpsertRowExecutor"/>
/// writing to <b>real Azure Table Storage</b>. Drives the ADR-0016 provider
/// conformance proof that ADR 0012's double-apply gap is closed: a crash
/// between the ring/outbox commit and the row write recovers effectively-once.
/// </summary>
public sealed class AzureUpsertRowRecoveryClusterFixture : IAsyncLifetime
{
    static string _connectionString = "";
    static TableServiceClient _tableServiceClient = null!;
    static BlobServiceClient _blobServiceClient = null!;

    AzuriteContainer _azurite = null!;

    public TestCluster Cluster { get; private set; } = null!;

    public TableServiceClient TableServiceClient => _tableServiceClient;

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
        _tableServiceClient = new TableServiceClient(_connectionString);
        _blobServiceClient = new BlobServiceClient(_connectionString);

        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async Task DisposeAsync()
    {
        if (Cluster is not null)
            await Cluster.DisposeAsync();
        if (_azurite is not null)
            await _azurite.DisposeAsync();
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
            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Services.AddSingleton(_tableServiceClient);
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(
                _ => new AzureTableWriteStoreFactory(_tableServiceClient));
            siloBuilder.Services.AddSingleton(TimeProvider.System);
            siloBuilder.Services.Configure<EdictOptions>(o =>
            {
                // Small deterministic backoff; default MaxAttempts so the
                // failing window never dead-letters before recovery.
                o.OutboxBaseDelay = TimeSpan.FromMilliseconds(200);
                o.OutboxJitterFraction = 0;
            });
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, PublishEventExecutor>();
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, AzureControllableUpsertRowExecutor>();
            siloBuilder.Services.AddSingleton<IDeadLetterPromoter, DeadLetterPromoter>();
            // Custom Azure provider wiring (test-only AzureControllableUpsertRowExecutor),
            // so the fixture registers wiring markers manually rather than
            // calling AddEdictAzureStreams/Persistence.
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
            siloBuilder.AddEdict();
            siloBuilder.UseInMemoryReminderService();
            // PubSubStore stays on memory storage — Orleans's internal
            // pub-sub state is out of scope for the Edict substrate story
            // (ADR 0025 keeps it on Tables in production).
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            // edict-state on Azure Blob (ADR 0025) — provider conformance
            // tests must exercise the same substrate the sample silo wires.
            siloBuilder.AddAzureBlobGrainStorage("edict-state", options =>
            {
                options.BlobServiceClient = _blobServiceClient;
                options.ContainerName = "edict-state";
            });
            siloBuilder.AddAzureQueueStreams("edict", configure =>
            {
                configure.ConfigureAzureQueue(opt => opt.Configure(o =>
                {
                    o.QueueServiceClient = new QueueServiceClient(_connectionString);
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
