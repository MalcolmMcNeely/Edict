using Azure.Storage.Queues;

using Edict.Contracts.Sending;
using Edict.Core.Grains;
using Edict.Core.Serialization;
using Edict.Generated;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Configuration;
using Orleans.Serialization;
using Orleans.TestingHost;

using Testcontainers.Azurite;

namespace Edict.Telemetry.Tests;

public sealed class TelemetryClusterFixture : IAsyncLifetime
{
    private static string _queueConnectionString = "";
    private AzuriteContainer _azurite = null!;

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
        _queueConnectionString = _azurite.GetConnectionString();

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
            .AddAssembly(typeof(TelOrderGrain).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryGrainStorage("edict-dedup");
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
