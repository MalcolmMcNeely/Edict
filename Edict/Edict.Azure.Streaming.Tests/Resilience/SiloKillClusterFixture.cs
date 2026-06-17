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
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.TestingHost;

using Testcontainers.Azurite;

using Xunit;

namespace Edict.Azure.Streaming.Tests.Resilience;

// Owns its own Azurite container and runs the default multi-silo cluster:
// KillSiloAsync permanently mutates membership, and Orleans' stream PubSub
// bookkeeping doesn't reconverge cleanly enough for other transport-fault tests
// to share. The broker is the redelivery driver; persistence is the dumb
// in-memory reference, shared across the in-process silos so the projection row
// the proof reads back survives the kill (it is a plain dictionary, not a grain).
public sealed class SiloKillClusterFixture : IAsyncLifetime
{
    AzuriteContainer _azurite = null!;
    string _connectionString = "";
    ReferenceTableStoreFactory _tableStoreFactory = null!;
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
        _tableStoreFactory = new ReferenceTableStoreFactory();

        var context = new ResilienceContext(
            _connectionString, _tableStoreFactory, new ReferenceClaimCheckStore());
        _contextKey = ResilienceContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
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

    // Lets a test target the kill at the silo that actually owns the in-flight
    // activation, captured by the slow projection on Handle entry.
    public SiloHandle FindSiloByAddress(SiloAddress address)
    {
        if (Cluster.Primary is { } primary && primary.SiloAddress.Equals(address))
        {
            return primary;
        }
        var match = Cluster.SecondarySilos.FirstOrDefault(
            s => s.SiloAddress.Equals(address));
        return match ?? throw new InvalidOperationException(
            $"No SiloHandle in the cluster matches address {address}.");
    }

    // Reads the projection row back from the shared reference table store — the
    // one row the silo-kill proof asserts. A streaming property (the row settles
    // once under redelivery) read off the reference persistence the issue scopes
    // it to, not a real-store assertion.
    public async Task<SiloKillTableRow?> GetProjectionRowAsync(Guid aggregateId)
    {
        var store = await _tableStoreFactory.CreateAsync<SiloKillTableRow>(SiloKillProjectionBuilder.Table);
        return await store.GetAsync(aggregateId.ToString("N"), aggregateId.ToString());
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(SiloKillProjectionEvent).Assembly)
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
                    // Short visibility timeout so the killed silo's unacked message
                    // returns to visible and a surviving silo redelivers it.
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
public sealed class SiloKillCollection : ICollectionFixture<SiloKillClusterFixture>
{
    public const string Name = "SiloKill";
}
