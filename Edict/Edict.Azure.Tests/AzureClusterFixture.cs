using Azure.Data.Tables;
using Azure.Storage.Queues;

using Edict.Azure.TableStorage;
using Edict.Contracts.Sending;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Generated;

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

    private AzuriteContainer _azurite = null!;

    public TestCluster Cluster { get; private set; } = null!;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

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
            siloBuilder.Services.AddEdictOutbox();
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryGrainStorage("edict-dedup");
            siloBuilder.AddMemoryGrainStorage("edict-state");
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
            clientBuilder.Services.AddEdict();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class AzureClusterCollection : ICollectionFixture<AzureClusterFixture>
{
    public const string Name = "AzureCluster";
}
