using Azure.Storage.Queues;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.Sending;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Tests.Conformance.Streaming.References;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Configuration;
using Orleans.Serialization;
using Orleans.TestingHost;

using Testcontainers.Azurite;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Resilience;

// Owns its own Azurite container instead of sharing AzuriteAssemblyHost: pausing
// it mid-test would corrupt every parallel collection running against the shared
// instance. Only the AQS queue (the streaming axis) is the fault point —
// persistence is the dumb in-memory reference, so a paused Azurite cannot perturb
// it. Single silo: the transport-fault proofs do not exercise membership.
public sealed class ResilienceClusterFixture : IAsyncLifetime
{
    AzuriteContainer _azurite = null!;
    string _connectionString = "";
    string _contextKey = "";

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

        var context = new ResilienceContext(
            _connectionString, new ReferenceTableStoreFactory(), new ReferenceClaimCheckStore());
        _contextKey = ResilienceContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Options.InitialSilosCount = 1;
        builder.Properties[ResilienceContextRegistry.ContextKeyProperty] = _contextKey;
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
        ResilienceContextRegistry.Unregister(_contextKey);
        if (_azurite is not null)
        {
            await _azurite.DisposeAsync();
        }
    }

    // Pause preserves the host port binding so the framework's reconnect path is
    // what's exercised, not a host-port rebind on the next test.
    public async Task PauseAzuriteAsync() => await _azurite.PauseAsync();

    public async Task UnpauseAzuriteAsync() => await _azurite.UnpauseAsync();

    // Tests call this on entry so the fixture starts from a known-good baseline
    // even if a previous test panicked mid pause.
    public async Task EnsureRunningAsync()
    {
        if (_azurite.State == DotNet.Testcontainers.Containers.TestcontainersStates.Paused)
        {
            await _azurite.UnpauseAsync();
        }
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(ResilienceTestEvent).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[ResilienceContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing from silo configuration.");
            var ctx = ResilienceContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Configure<SiloMessagingOptions>(o => o.ResponseTimeout = TimeSpan.FromMinutes(2));
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(ctx.TableStoreFactory);
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(ctx.ClaimCheckStore);
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
            siloBuilder.AddEdict();
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryGrainStorage("edict-state");
            siloBuilder.AddAzureQueueStreams("edict", configure =>
            {
                configure.ConfigureAzureQueue(opt => opt.Configure(o =>
                {
                    o.QueueServiceClient = new QueueServiceClient(ctx.ConnectionString);
                    // Span at least one queue visibility timeout so a paused
                    // Azurite produces an observable redelivery within the budget.
                    o.MessageVisibilityTimeout = TimeSpan.FromSeconds(5);
                }));
                configure.ConfigurePullingAgent(opt => opt.Configure(o =>
                    o.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(200)));
            });
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ResilienceCollection : ICollectionFixture<ResilienceClusterFixture>
{
    public const string Name = "Resilience";
}
