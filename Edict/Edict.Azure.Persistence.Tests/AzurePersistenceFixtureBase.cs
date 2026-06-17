using Azure.Data.Tables;
using Azure.Storage.Blobs;

using Edict.Azure.Persistence.Audit;
using Edict.Azure.Persistence.TableStorage;
using Edict.Azure.Streaming.ClaimCheck;
using Edict.Contracts.Audit;
using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Audit;
using Edict.Core.ClaimCheck;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Core.Tenancy;
using Edict.Tests.Conformance;
using Edict.Tests.Conformance.Tenancy;
using Edict.Tests.Conformance.Outbox;
using Edict.Tests.Conformance.Persistence;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans;
using Orleans.Configuration;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Azure.Persistence.Tests;

/// <summary>
/// Shared bring-up for every Azure persistence-axis conformance fixture: a silo
/// with a <strong>dumb deliver-once <c>MemoryStreams</c> reference</strong>
/// behind <c>"edict"</c> and <strong>real Azure persistence</strong> over the
/// assembly-shared Azurite — Azure Blob grain storage, the Azure Table read/write
/// store, the literal dead-letter table, and a real Azure Blob claim-check store.
/// Subclasses pick the per-shape knobs (outbox options, controllable
/// publish-executor, controllable grain storage, claim-check threshold) by
/// overriding the virtual hooks; one configurator applies them. The reference
/// stream is never asserted upon — the surface only exposes the send seam, grain
/// probes, and the durable table seams, so a persistence scenario cannot assert a
/// streaming property.
/// </summary>
public abstract class AzurePersistenceFixtureBase : PersistenceConformanceFixture
{
    string _connectionString = "";
    TableServiceClient _tableServiceClient = null!;
    BlobServiceClient _blobServiceClient = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public override IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public override IGrainFactory GrainFactory => Cluster.GrainFactory;

    public override IEdictTableWriteStore<T> GetTableStore<T>(string tableName) =>
        new AzureTableWriteStore<T>(_tableServiceClient.GetTableClient(tableName));

    public override IEdictTableStoreFactory TableStoreFactory =>
        new AzureTableWriteStoreFactory(_tableServiceClient);

    protected BlobServiceClient BlobServiceClient => _blobServiceClient;

    protected TableServiceClient TableServiceClient => _tableServiceClient;

    internal Serializer ClientSerializer => Cluster.Client.ServiceProvider.GetRequiredService<Serializer>();

    // IEdictClaimCheckStore is internal, so this is private-protected — the
    // claim-check subclasses that read it are derived and in this assembly.
    private protected IEdictClaimCheckStore ClaimCheckStore { get; private set; } = null!;

    protected string ClaimCheckContainerName { get; private set; } = "";

    protected string GrainStateContainerName { get; private set; } = "";

    protected string AuditTableName { get; private set; } = "";

    protected string AuditPayloadContainerName { get; private set; } = "";

    // Per-shape knobs — a subclass overrides only the ones it needs.
    protected virtual Action<EdictOptions>? ConfigureOptions => null;

    protected virtual bool ReplacePublishExecutorWithControllable => false;

    protected virtual bool DecorateGrainStorage => false;

    protected virtual int? ClaimCheckThresholdBytes => null;

    // A schedule fixture returns a FakeTimeProvider here so its scenarios can push
    // a schedule past-due on a virtual clock; every other fixture leaves it null
    // and runs on TimeProvider.System.
    protected virtual TimeProvider? ClockOverride => null;

    // The audit fixture turns this on to wire the Azure audit stores + WithAudit and
    // an origin resolver on the silo and client; every other fixture leaves it off
    // and never captures.
    protected virtual bool EnableAudit => false;

    // The tenancy fixture turns this on to wire AddEdictTenant on the silo and client,
    // arming origin stamping, the ambient-scoped reader, and the isolation call filter;
    // every other fixture leaves it off and pays no tenant tax.
    protected virtual bool EnableTenancy => false;

    // The known principal the audit fixture's origin resolver yields, so a conformance
    // scenario can assert attribution. Static because the nested configurators
    // (constructed by Orleans) reach it; the audit fixture surfaces it as the instance
    // IAuditConformanceFixture.AuditPrincipal.
    internal static EdictPrincipal KnownAuditPrincipal => EdictPrincipal.Of("auditor-bob");

    public override async Task InitializeAsync()
    {
        _connectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();
        _tableServiceClient = new TableServiceClient(_connectionString);
        _blobServiceClient = new BlobServiceClient(_connectionString);

        var token = Guid.NewGuid().ToString("N");
        GrainStateContainerName = $"edict-state-{token}";
        ClaimCheckContainerName = $"edict-claim-check-{token}";
        AuditTableName = $"edictauditrecord{token}";
        AuditPayloadContainerName = $"edict-audit-payload-{token}";

        // Built eagerly off the grain task scheduler — a sync-over-async path in
        // a lazy singleton factory deadlocks first-time container creation.
        ClaimCheckStore = await AzureBlobClaimCheckStore.CreateAsync(
            _blobServiceClient, ClaimCheckContainerName);

        if (EnableAudit)
        {
            // Provision the audit table + payload container off the grain scheduler,
            // so the silo-side singleton factories do no first-resolve I/O.
            await AzureTableAuditStore.ProvisionAsync(_tableServiceClient, AuditTableName);
            await AzureBlobAuditPayloadStore.ProvisionAsync(_blobServiceClient, AuditPayloadContainerName);
        }

        var context = new AzurePersistenceContext(
            _tableServiceClient,
            _blobServiceClient,
            GrainStateContainerName,
            ClaimCheckStore,
            ConfigureOptions,
            ReplacePublishExecutorWithControllable,
            DecorateGrainStorage,
            ClaimCheckThresholdBytes,
            OutboxFault,
            StorageFault,
            ClockOverride,
            EnableAudit,
            AuditTableName,
            AuditPayloadContainerName,
            EnableTenancy);
        _contextKey = AzurePersistenceContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Properties[AzurePersistenceContextRegistry.ContextKeyProperty] = _contextKey;
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
        AzurePersistenceContextRegistry.Unregister(_contextKey);
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
            var key = siloBuilder.Configuration[AzurePersistenceContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing from silo configuration.");
            var ctx = AzurePersistenceContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            // Azurite first-time blob/table provisioning under a burst of cold
            // grain activations can run past Orleans' default 30s response timeout.
            siloBuilder.Configure<SiloMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromMinutes(2));
            siloBuilder.Services.AddSingleton(ctx.TableServiceClient);
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(
                _ => new AzureTableWriteStoreFactory(ctx.TableServiceClient));
            siloBuilder.Services.AddSingleton(ctx.ClockOverride ?? TimeProvider.System);
            siloBuilder.Services.AddSingleton(ctx.ClaimCheckStore);

            if (ctx.ClaimCheckThresholdBytes is int thresholdBytes)
            {
                // A low threshold forces every raised event onto the pointer
                // branch, exercising the receiver-unwrap path on the real store.
                siloBuilder.Services.AddSingleton(serviceProvider => new ClaimCheckPolicy(
                    serviceProvider.GetRequiredService<Serializer>(),
                    thresholdBytes: thresholdBytes,
                    store: serviceProvider.GetRequiredService<IEdictClaimCheckStore>(),
                    accessors: serviceProvider.GetRequiredService<IEventStreamAccessors>()));
            }

            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();

            if (ctx.ConfigureOptions is { } configureOptions)
            {
                siloBuilder.AddEdict(configureOptions);
            }
            else
            {
                siloBuilder.AddEdict();
            }

            if (ctx.ReplacePublishExecutorWithControllable)
            {
                ControllableOutboxExecutor.Replace(siloBuilder.Services, ctx.OutboxFault);
            }

            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddAzureBlobGrainStorage("edict-state", options =>
            {
                options.BlobServiceClient = ctx.BlobServiceClient;
                options.ContainerName = ctx.GrainStateContainerName;
            });

            if (ctx.DecorateGrainStorage)
            {
                ControllableGrainStorage.Decorate(siloBuilder.Services, ctx.StorageFault);
            }

            // The dumb deliver-once reference stream: MemoryStreams delivers each
            // publish once to its implicit subscribers, carries the EventId
            // intact, and never redelivers — the persistence axis never asserts a
            // streaming property of it.
            siloBuilder.AddMemoryStreams("edict");

            // Turn on audit capture over the Azure audit stores. The table and
            // container are already provisioned by the fixture, so the factories do
            // no first-resolve I/O.
            if (ctx.EnableAudit)
            {
                siloBuilder.Services.AddSingleton<IEdictAuditStore>(serviceProvider =>
                    AzureTableAuditStore.Create(ctx.TableServiceClient, serviceProvider.GetRequiredService<Serializer>(), ctx.AuditTableName));
                siloBuilder.Services.AddSingleton<IEdictAuditPayloadStore>(
                    _ => AzureBlobAuditPayloadStore.Create(ctx.BlobServiceClient, ctx.AuditPayloadContainerName));
                siloBuilder.Services.AddEdictAudit(_ => KnownAuditPrincipal);
                siloBuilder.WithAudit();
            }

            // Arm tenancy on the silo so a relayed send inside a tenant turn stamps the
            // inherited tenant and the isolation call filter guards command grains.
            if (ctx.EnableTenancy)
            {
                siloBuilder.Services.AddEdictTenant(_ => ConformanceTenantSource.Current);
            }
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Configure<ClientMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromMinutes(2));
            clientBuilder.Services.AddEdict();

            var key = configuration[AzurePersistenceContextRegistry.ContextKeyProperty];
            var context = key is not null ? AzurePersistenceContextRegistry.Get(key) : null;
            if (context?.EnableAudit == true)
            {
                // The client is the originating send, so it needs the resolver to
                // stamp the principal before the command leaves for the silo.
                clientBuilder.Services.AddEdictAudit(_ => KnownAuditPrincipal);
            }

            if (context?.EnableTenancy == true)
            {
                // The client is the originating send and the read edge, so it resolves
                // the ambient tenant for both the send stamp and the scoped read.
                clientBuilder.Services.AddEdictTenant(_ => ConformanceTenantSource.Current);
            }
        }
    }
}
