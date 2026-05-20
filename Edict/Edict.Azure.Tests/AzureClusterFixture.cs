using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure.DeadLetter;
using Edict.Azure.TableStorage;
using Edict.Contracts.Sending;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.TestingHost;

using Testcontainers.Azurite;

namespace Edict.Azure.Tests;

/// <summary>
/// Full-stack Azurite-backed cluster fixture for the Azure provider test suite
/// (ADR 0016). Uses real Azure Queue Storage streams and Azure Table Storage so
/// tests prove the full at-least-once + dedup path and the table write seam.
/// </summary>
public sealed class AzureClusterFixture : IAsyncLifetime
{
    private static string _connectionString = "";
    private static TableServiceClient _tableServiceClient = null!;
    private static BlobServiceClient _blobServiceClient = null!;

    private AzuriteContainer _azurite = null!;

    public TestCluster Cluster { get; private set; } = null!;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public TableServiceClient TableServiceClient => _tableServiceClient;

    public BlobServiceClient BlobServiceClient => _blobServiceClient;

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

    private static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(AzureOrderCommandHandler).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Services.AddSingleton(_tableServiceClient);
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(
                _ => new AzureTableWriteStoreFactory(_tableServiceClient));
            // AddEdict() registers the receiver-side ClaimCheckUnwrap that
            // every EdictIdempotencyBase consumer resolves on the stream
            // observer path (ADR 0024, slice 3). Without it, every stream
            // delivery throws before the consumer's Handle runs.
            siloBuilder.Services.AddEdict();
            siloBuilder.Services.AddEdictOutbox();
            siloBuilder.UseInMemoryReminderService();
            // PubSubStore stays on memory storage in this fixture — Orleans's
            // internal pub-sub state is out of scope for the Edict substrate
            // story (ADR 0025 keeps it on Tables in production, but the
            // provider-conformance suite isolates the change under test).
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            // edict-state on Azure Blob (ADR 0025). Substrate-behaviour tests
            // (including GrainStateOnBlobSubstrateAtomicityTests) exercise the
            // same provider the sample silo wires in production.
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
                    // Short visibility timeout lets the at-least-once redelivery
                    // test observe a real queue re-queue within seconds.
                    o.MessageVisibilityTimeout = TimeSpan.FromSeconds(5);
                }));
                configure.ConfigurePullingAgent(opt => opt.Configure(o =>
                    o.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(200)));
            });
        }
    }

    private sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            IClientBuilder clientBuilder)
        {
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddSingleton(_tableServiceClient);
            clientBuilder.Services.AddEdict();
            clientBuilder.Services.AddEdictAzureDeadLetterRepository();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class AzureClusterCollection : ICollectionFixture<AzureClusterFixture>
{
    public const string Name = "AzureCluster";
}
