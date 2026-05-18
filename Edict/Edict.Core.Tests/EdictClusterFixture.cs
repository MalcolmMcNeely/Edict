using Azure.Data.Tables;
using Azure.Storage.Queues;

using Edict.Contracts.Sending;
using Edict.Core.Grains;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Core.Tests.Grains;
using Edict.Core.Tests.TableStorage;
using Edict.Generated;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Configuration;
using Orleans.Serialization;
using Orleans.TestingHost;

using Testcontainers.Azurite;

namespace Edict.Core.Tests;

/// <summary>
/// A real in-memory Orleans cluster backed by a local Azurite container for
/// Azure Queue Storage streams. Starting with this slice the cluster uses the
/// "edict" stream provider so Command Handlers can <c>Raise</c> events that
/// land on real domain streams (Azurite/Testcontainers). The client gets the
/// generated <c>AddEdict()</c>, so a test resolves <see cref="IEdictSender"/>
/// exactly as a consumer would.
/// </summary>
public sealed class EdictClusterFixture : IAsyncLifetime
{
    // Set before cluster construction so the nested configurator classes can read it.
    private static string _queueConnectionString = "";
    private static TableServiceClient _tableServiceClient = null!;
    private static readonly InMemoryTableStoreFactory _tableStoreFactory = new();

    private AzuriteContainer _azurite = null!;

    public TestCluster Cluster { get; private set; } = null!;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public TableServiceClient TableServiceClient => _tableServiceClient;

    /// <summary>
    /// In-memory write-store factory shared between the silo and test code.
    /// Tests can call <see cref="InMemoryTableStoreFactory.GetStore{T}"/> to read
    /// rows written by grains without going through Azure.
    /// </summary>
    public InMemoryTableStoreFactory TableStoreFactory => _tableStoreFactory;

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
            .AddAssembly(typeof(OrderGrain).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Services.AddSingleton<IValidator<ValidateSkuCommand>, SkuRequiredValidator>();
            siloBuilder.Services.AddSingleton<IValidator<StateCheckCommand>, GrainStateRequiredValidator>();
            siloBuilder.Services.AddSingleton(_tableServiceClient);
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(_tableStoreFactory);
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
