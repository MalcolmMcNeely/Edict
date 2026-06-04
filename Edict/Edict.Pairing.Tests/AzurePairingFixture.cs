using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure.Persistence;
using Edict.Azure.Streaming;
using Edict.Contracts.Sending;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Tests.Conformance;
using Edict.Tests.Conformance.Outbox;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Configuration;
using Orleans.Serialization;
using Orleans.TestingHost;

using Xunit;

namespace Edict.Pairing.Tests;

/// <summary>
/// The AQS+Azure shipped pairing: a single silo wired through the documented
/// consumer stack — <c>AddEdictAzureStreams</c> + <c>AddEdictAzurePersistence</c>
/// + <c>AddEdictAzureBlobClaimCheck</c> — over the assembly-shared Azurite, with
/// the real Azure Blob <c>edict-state</c> store wrapped in
/// <see cref="ControllableGrainStorage"/>. Boot proves the documented composition
/// with both real providers' types present actually stands up and round-trips; the
/// conjunction faults the real Blob write and lets the real AQS stream redeliver.
/// </summary>
public sealed class AzurePairingFixture : PairingFixture
{
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public override IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public override IGrainFactory GrainFactory => Cluster.GrainFactory;

    public override async Task InitializeAsync()
    {
        var connectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        var context = new AzurePairingContext(
            connectionString,
            ClaimCheckContainerName: $"edict-claim-check-{Guid.NewGuid():N}",
            StorageFault: StorageFault);
        _contextKey = PairingContextRegistry<AzurePairingContext>.Register(context);

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Properties[PairingContextRegistry<AzurePairingContext>.ContextKeyProperty] = _contextKey;
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
        PairingContextRegistry<AzurePairingContext>.Unregister(_contextKey);
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
            var key = siloBuilder.Configuration[PairingContextRegistry<AzurePairingContext>.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing from silo configuration.");
            var ctx = PairingContextRegistry<AzurePairingContext>.Get(key);

            var queueServiceClient = new QueueServiceClient(ctx.ConnectionString);
            var blobServiceClient = new BlobServiceClient(ctx.ConnectionString);
            var tableServiceClient = new TableServiceClient(ctx.ConnectionString);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            // First-time Azurite blob/table/queue provisioning under a burst of
            // cold activations can run past Orleans' default 30s response timeout.
            siloBuilder.Configure<SiloMessagingOptions>(o => o.ResponseTimeout = TimeSpan.FromMinutes(2));
            siloBuilder.Services.AddSingleton(queueServiceClient);
            siloBuilder.Services.AddSingleton(blobServiceClient);
            siloBuilder.Services.AddSingleton(tableServiceClient);

            siloBuilder.AddEdict();
            siloBuilder.AddEdictAzureStreams(o =>
            {
                o.QueueServiceClient = queueServiceClient;
                o.QueuePollingPeriod = TimeSpan.FromMilliseconds(200);
            });
            // The conjunction's faulted consumer turn leaves the queue message
            // unacked; a short visibility timeout makes the redelivery observable
            // within the test budget. AddEdictAzureStreams does not surface it.
            siloBuilder.Services.Configure<AzureQueueOptions>("edict", queueOptions =>
                queueOptions.MessageVisibilityTimeout = TimeSpan.FromSeconds(5));
            siloBuilder.AddEdictAzurePersistence(o =>
            {
                o.TableServiceClient = tableServiceClient;
                o.BlobServiceClient = blobServiceClient;
            });
            siloBuilder.AddEdictAzureBlobClaimCheck(o =>
            {
                o.BlobServiceClient = blobServiceClient;
                o.ContainerName = ctx.ClaimCheckContainerName;
            });
            // Wrap the real Azure Blob edict-state store so the conjunction can
            // fault a real grain-state write; transparent while the fixture's
            // switch is off.
            ControllableGrainStorage.Decorate(siloBuilder.Services, ctx.StorageFault, "edict-state");
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Configure<ClientMessagingOptions>(o => o.ResponseTimeout = TimeSpan.FromMinutes(2));
            clientBuilder.Services.AddEdict();
        }
    }
}

sealed record AzurePairingContext(
    string ConnectionString, string ClaimCheckContainerName, StorageFaultState StorageFault);

[CollectionDefinition(Name)]
public sealed class AzurePairingCollection : ICollectionFixture<AzurePairingFixture>
{
    public const string Name = "AzurePairing";
}
