using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure.TableStorage;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Azure.Tests.Outbox;

/// <summary>
/// Azurite-backed cluster wired with a flippable
/// <see cref="AzureControllableOutboxExecutor"/> (PublishEvent kind) so the
/// lifted Outbox tests (issue #88) can drive a crash between the ring/outbox
/// commit and the publish over the <b>real Azure Queue + Azure Blob</b> stack,
/// then a recovery drain. Small deterministic backoff lets the recovery
/// scenarios finish in seconds without ever dead-lettering (default
/// <see cref="EdictOptions.OutboxMaxAttempts"/>). Dead-letter promotion tests
/// override <c>OutboxMaxAttempts</c> via their own scenario configuration.
/// Shares the assembly-scoped Azurite (<see cref="AzuriteAssemblyHost"/>) with
/// per-fixture Guid-prefixed container/table names.
/// </summary>
public sealed class AzureOutboxRecoveryClusterFixture : IAsyncLifetime
{
    string _connectionString = "";
    TableServiceClient _tableServiceClient = null!;
    BlobServiceClient _blobServiceClient = null!;
    QueueServiceClient _queueServiceClient = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public TableServiceClient TableServiceClient => _tableServiceClient;

    public BlobServiceClient BlobServiceClient => _blobServiceClient;

    public QueueServiceClient QueueServiceClient => _queueServiceClient;

    public string GrainStateContainerName { get; private set; } = "";

    public string DeadLetterTableName { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _connectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        _tableServiceClient = new TableServiceClient(_connectionString);
        _blobServiceClient = new BlobServiceClient(_connectionString);
        _queueServiceClient = new QueueServiceClient(_connectionString);

        var token = Guid.NewGuid().ToString("N");
        GrainStateContainerName = $"edict-state-{token}";
        DeadLetterTableName = $"deadletter{token}";

        var context = new AzureClusterContext(
            _connectionString,
            _tableServiceClient,
            _blobServiceClient,
            _queueServiceClient,
            GrainStateContainerName,
            DeadLetterTableName);
        _contextKey = AzureClusterContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Properties[AzureClusterContextRegistry.ContextKeyProperty] = _contextKey;
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
        AzureClusterContextRegistry.Unregister(_contextKey);
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
            var key = siloBuilder.Configuration[AzureClusterContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException(
                    "ClusterContextKey missing from silo configuration.");
            var ctx = AzureClusterContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Services.AddSingleton(ctx.TableServiceClient);
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(
                _ => new AzureTableWriteStoreFactory(ctx.TableServiceClient));
            siloBuilder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(_ =>
                new AzureTableRepository<EdictDeadLetterEntry>(
                    ctx.TableServiceClient, ctx.DeadLetterTableName));
            siloBuilder.Services.AddSingleton(TimeProvider.System);
            // Custom Azure provider wiring (test-only controllable executor), so
            // the fixture registers the wiring markers manually rather than
            // calling AddEdictAzureStreams/Persistence.
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
            // siloBuilder.AddEdict wires AddEdictOutbox under the hood, which
            // registers PublishEventExecutor. The OutboxHost ctor's ToDictionary
            // on OutboxEffectKind throws on duplicate keys — so we have to
            // *replace* the auto-registered PublishEventExecutor descriptor
            // with the controllable one rather than appending.
            siloBuilder.AddEdict(o =>
            {
                // Small deterministic backoff so the recovery drain triggers in
                // seconds. Default MaxAttempts keeps the failing window from
                // dead-lettering before the controllable flip heals.
                o.OutboxBaseDelay = TimeSpan.FromMilliseconds(200);
                o.OutboxJitterFraction = 0;
            });
            ReplacePublishEventExecutorWithControllable(siloBuilder.Services);
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddAzureBlobGrainStorage("edict-state", options =>
            {
                options.BlobServiceClient = ctx.BlobServiceClient;
                options.ContainerName = ctx.GrainStateContainerName;
            });
            siloBuilder.AddAzureQueueStreams("edict", configure =>
            {
                configure.ConfigureAzureQueue(opt => opt.Configure(o =>
                {
                    o.QueueServiceClient = new QueueServiceClient(ctx.ConnectionString);
                    o.MessageVisibilityTimeout = TimeSpan.FromSeconds(5);
                }));
                configure.ConfigurePullingAgent(opt => opt.Configure(o =>
                    o.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(200)));
            });
        }

        static void ReplacePublishEventExecutorWithControllable(IServiceCollection services)
        {
            var publish = services.Single(d =>
                d.ServiceType == typeof(IOutboxEffectExecutor)
                && d.ImplementationType == typeof(PublishEventExecutor));
            services.Remove(publish);
            services.AddSingleton<IOutboxEffectExecutor, AzureControllableOutboxExecutor>();
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
public sealed class AzureOutboxControllableExecutorCollection
    : ICollectionFixture<AzureOutboxRecoveryClusterFixture>,
      ICollectionFixture<AzureOutboxDeadLetterClusterFixture>,
      ICollectionFixture<AzureOutboxReminderPeriodClusterFixture>
{
    // One shared xUnit collection for every test that touches the
    // process-wide AzureControllableOutboxExecutor static flag. Three
    // fixtures, three TestCluster shapes (default backoff, MaxAttempts=2,
    // configured drain reminder period) — but tests in the same xUnit
    // collection serialise, so the static ShouldFail / FailedAttempts cannot
    // race across the recovery, dead-letter, and reminder-period scenarios.
    public const string Name = "AzureOutboxControllableExecutor";
}
