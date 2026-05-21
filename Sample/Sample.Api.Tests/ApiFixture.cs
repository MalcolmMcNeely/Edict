using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure.TableStorage;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.TestingHost;

using Sample.Contracts.Orders.Projections;
using Sample.Contracts.Payments.Projections;
using Sample.Silo.Orders.CommandHandlers;

using Testcontainers.Azurite;

using Xunit;

namespace Sample.Api.Tests;

public sealed class ApiFixture : IAsyncLifetime
{
    private static string _queueConnectionString = "";
    private static TableServiceClient _tableServiceClient = null!;
    private static BlobServiceClient _blobServiceClient = null!;

    private AzuriteContainer _azurite = null!;
    private TestCluster _cluster = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public HttpClient Client { get; private set; } = null!;

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
        _queueConnectionString = _azurite.GetConnectionString();
        _tableServiceClient = new TableServiceClient(_queueConnectionString);
        _blobServiceClient = new BlobServiceClient(_queueConnectionString);

        var clusterBuilder = new TestClusterBuilder();
        clusterBuilder.AddSiloBuilderConfigurator<SiloConfigurator>();
        clusterBuilder.AddClientBuilderConfigurator<ClientConfigurator>();
        _cluster = clusterBuilder.Build();
        await _cluster.DeployAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.UseEnvironment("Testing");
                host.ConfigureServices(services =>
                {
                    services.AddSingleton<IClusterClient>(_cluster.Client);
                    services.AddSingleton<IGrainFactory>(_cluster.Client);
                    services.AddSingleton(_tableServiceClient);
                    services.AddSingleton<IEdictTableRepository<OrderStatusRow>>(
                        _ => new AzureTableRepository<OrderStatusRow>(
                            _tableServiceClient, "ordersbystatus"));
                    services.AddSingleton<IEdictTableRepository<OrderOutcomeRow>>(
                        _ => new AzureTableRepository<OrderOutcomeRow>(
                            _tableServiceClient, "orderoutcome"));
                });
            });

        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await _cluster.DisposeAsync();
        await _azurite.DisposeAsync();
    }

    private static void ConfigureEdictSerialization(ISerializerBuilder ser)
    {
        ser.AddAssembly(typeof(OrderCommandHandler).Assembly);
        ser.AddAssembly(typeof(IEdictCommandHandler).Assembly);
        ser.AddEdictContractSerializer();
    }

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Services.AddSingleton(_tableServiceClient);
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(
                _ => new AzureTableWriteStoreFactory(_tableServiceClient));
            siloBuilder.AddAzureTableGrainStorage("PubSubStore", options =>
                options.TableServiceClient = _tableServiceClient);
            siloBuilder.AddAzureBlobGrainStorage("edict-state", options =>
            {
                options.BlobServiceClient = _blobServiceClient;
                options.ContainerName = "edict-state";
            });
            siloBuilder.Services.AddEdict();
            siloBuilder.Services.AddEdictOutbox();
            siloBuilder.UseAzureTableReminderService(options =>
                options.TableServiceClient = _tableServiceClient);
            siloBuilder.AddAzureQueueStreams("edict", configure =>
            {
                configure.ConfigureAzureQueue(opt => opt.Configure(o =>
                    o.QueueServiceClient = new QueueServiceClient(_queueConnectionString)));
                configure.ConfigurePullingAgent(opt => opt.Configure(o =>
                    o.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(200)));
            });
        }
    }

    private sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder) =>
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
    }
}
