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

using Testcontainers.Azurite;

namespace Edict.Azure.Tests;

/// <summary>
/// Conformance cluster for the new ADR-0028 silo-builder extensions:
/// boots a real Azurite, then wires the silo through
/// <c>silo.AddEdict()</c> + <c>silo.AddEdictAzureStreams()</c> +
/// <c>silo.AddEdictAzurePersistence()</c> only (no manual provider
/// registrations) so a passing round-trip is proof that the consumer-facing
/// three-call shape wires Orleans correctly end-to-end.
/// </summary>
public sealed class EdictAzureSiloBuilderExtensionsClusterFixture : IAsyncLifetime
{
    static string _connectionString = "";
    static TableServiceClient _tableServiceClient = null!;
    static BlobServiceClient _blobServiceClient = null!;
    static QueueServiceClient _queueServiceClient = null!;

    AzuriteContainer _azurite = null!;

    public TestCluster Cluster { get; private set; } = null!;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

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
        _queueServiceClient = new QueueServiceClient(_connectionString);

        var builder = new TestClusterBuilder();
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
        if (_azurite is not null)
        {
            await _azurite.DisposeAsync();
        }
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
            // Azurite first-time table creation can run past Orleans' default
            // 30s response timeout when the Edict-Azure storage + reminder
            // tables all need provisioning in parallel; bumping the timeout
            // gives the silo headroom for the first command roundtrip.
            siloBuilder.Configure<global::Orleans.Configuration.SiloMessagingOptions>(o =>
            {
                o.ResponseTimeout = TimeSpan.FromMinutes(2);
            });

            // The three new ISiloBuilder calls wire the consumer-facing surface
            // we want to prove (ADR 0028). Memory reminders + memory PubSub are
            // pre-registered so the test focuses on the Edict-specific Azurite
            // substrate (streams, grain-state blob, dead-letter table) rather
            // than co-validating Orleans' own Azure Table reminder/pubsub init
            // path that's already covered by AzureClusterFixture's broader
            // suite.
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");

            siloBuilder.AddEdict();
            siloBuilder.AddEdictAzureStreams(o =>
            {
                o.QueueServiceClient = _queueServiceClient;
            });
            siloBuilder.AddEdictAzurePersistence(o =>
            {
                o.TableServiceClient = _tableServiceClient;
                o.BlobServiceClient = _blobServiceClient;
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
