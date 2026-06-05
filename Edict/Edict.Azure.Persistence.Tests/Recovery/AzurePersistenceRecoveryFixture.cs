using System.Collections.Concurrent;

using Azure.Data.Tables;
using Azure.Storage.Blobs;

using Edict.Azure.Persistence.TableStorage;
using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Tests.Conformance;
using Edict.Tests.Conformance.Streaming.References;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans.Configuration;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Azure.Persistence.Tests.Recovery;

/// <summary>
/// The persistence-axis fixture for the bespoke Azure tests that the parameterised
/// battery base cannot host: real Azure Blob grain storage + Azure Table store behind
/// a <strong>dumb deliver-once <c>MemoryStreams</c></strong> reference, with the
/// auto-registered <c>UpsertRowExecutor</c> swapped for the controllable one so the
/// crash-then-recover gap-closure test can fault the row write. It also registers the
/// table-backed dead-letter repository on both silo and client (so the wiring test can
/// resolve the facade) and brings this assembly's own serialization metadata into the
/// silo so the renamed-row <c>[Alias]</c> survival test resolves its POCO. The reference
/// claim-check store only satisfies wiring — no recovery path touches it.
/// </summary>
public sealed class AzurePersistenceRecoveryFixture : IAsyncLifetime
{
    string _connectionString = "";
    TableServiceClient _tableServiceClient = null!;
    BlobServiceClient _blobServiceClient = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public TableServiceClient TableServiceClient => _tableServiceClient;

    public async Task InitializeAsync()
    {
        _connectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        _tableServiceClient = new TableServiceClient(_connectionString);
        _blobServiceClient = new BlobServiceClient(_connectionString);

        var token = Guid.NewGuid().ToString("N");
        var context = new RecoveryContext(
            _tableServiceClient,
            _blobServiceClient,
            $"edict-state-{token}",
            $"deadletter{token}");
        _contextKey = RecoveryContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Properties[RecoveryContextRegistry.ContextKeyProperty] = _contextKey;
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
        RecoveryContextRegistry.Unregister(_contextKey);
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(AzureRecoverableOrderRow).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[RecoveryContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing from silo configuration.");
            var ctx = RecoveryContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            // Azurite first-time blob/table provisioning under a burst of cold grain
            // activations can run past Orleans' default 30s response timeout.
            siloBuilder.Configure<SiloMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromMinutes(2));
            siloBuilder.Services.AddSingleton(ctx.TableServiceClient);
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(
                _ => new AzureTableWriteStoreFactory(ctx.TableServiceClient));
            siloBuilder.Services.AddSingleton(TimeProvider.System);
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(new ReferenceClaimCheckStore());
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();
            siloBuilder.AddEdict(o =>
            {
                o.OutboxBaseDelay = TimeSpan.FromMilliseconds(200);
                o.OutboxJitterFraction = 0;
            });
            // Replace the auto-registered UpsertRowExecutor with the controllable one.
            // Pre-registering before AddEdict() would append a duplicate
            // IOutboxEffectExecutor and the OutboxHost ctor's ToDictionary on
            // OutboxEffectKind would throw on the duplicate key.
            var upsert = siloBuilder.Services.Single(d =>
                d.ServiceType == typeof(IOutboxEffectExecutor)
                && d.ImplementationType == typeof(UpsertRowExecutor));
            siloBuilder.Services.Remove(upsert);
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor, AzureControllableUpsertRowExecutor>();
            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddAzureBlobGrainStorage("edict-state", options =>
            {
                options.BlobServiceClient = ctx.BlobServiceClient;
                options.ContainerName = ctx.GrainStateContainerName;
            });
            // The dumb deliver-once reference stream: the gap-closure test drives
            // redelivery by republishing by hand, so the persistence axis never relies
            // on a streaming property of it.
            siloBuilder.AddMemoryStreams("edict");
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            var key = configuration[RecoveryContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing from client configuration.");
            var ctx = RecoveryContextRegistry.Get(key);

            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Configure<ClientMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromMinutes(2));
            clientBuilder.Services.AddSingleton(ctx.TableServiceClient);
            // The dead-letter forensic facade reads through the projection grain;
            // AddEdict() registers the IEdictProjectionReader that backs it.
            clientBuilder.Services.AddEdict();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class AzurePersistenceRecoveryCollection : ICollectionFixture<AzurePersistenceRecoveryFixture>
{
    public const string Name = "AzurePersistenceRecovery";
}

sealed record RecoveryContext(
    TableServiceClient TableServiceClient,
    BlobServiceClient BlobServiceClient,
    string GrainStateContainerName,
    string DeadLetterTableName);

static class RecoveryContextRegistry
{
    public const string ContextKeyProperty = "Edict.Azure.Persistence.Tests.Recovery.ClusterContextKey";

    static readonly ConcurrentDictionary<string, RecoveryContext> _entries = new();

    public static string Register(RecoveryContext context)
    {
        var key = Guid.NewGuid().ToString("N");
        _entries[key] = context;
        return key;
    }

    public static RecoveryContext Get(string key) =>
        _entries.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"No recovery cluster context registered for key '{key}'.");

    public static void Unregister(string key) => _entries.TryRemove(key, out _);
}
