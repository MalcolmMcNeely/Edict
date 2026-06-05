using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.ClaimCheck;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Postgres;
using Edict.Postgres.ClaimCheck;
using Edict.Postgres.TableStorage;
using Edict.Tests.Conformance;
using Edict.Tests.Conformance.Outbox;
using Edict.Tests.Conformance.Persistence;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orleans;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Postgres.Tests;

/// <summary>
/// Shared bring-up for every Postgres persistence-axis conformance fixture: a
/// silo with a <strong>dumb deliver-once <c>MemoryStreams</c> reference</strong>
/// behind <c>"edict"</c> and <strong>real Postgres persistence</strong> over the
/// assembly-shared testcontainer (its own per-fixture database). The persistence
/// half is wired through the consumer-facing
/// <see cref="EdictPostgresSiloBuilderExtensions.AddEdictPostgresPersistence"/>,
/// so the battery exercises the same grain storage, dead-letter table,
/// claim-check store, table-store factory, and AdoNet reminder/PubSub the
/// documented composition gives a Postgres silo. Subclasses pick the per-shape
/// knobs (outbox options, controllable publish-executor, controllable grain
/// storage, claim-check threshold) by overriding the virtual hooks; one
/// configurator applies them. The reference stream is never asserted upon — the
/// surface only exposes the send seam, grain probes, and the durable table
/// seams, so a persistence scenario cannot assert a streaming property.
/// </summary>
public abstract class PostgresPersistenceFixtureBase : PersistenceConformanceFixture
{
    string _connectionString = "";
    NpgsqlDataSource _dataSource = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public override IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public override IGrainFactory GrainFactory => Cluster.GrainFactory;

    public override IEdictTableWriteStore<T> GetTableStore<T>(string tableName) =>
        new PostgresTableWriteStore<T>(
            _dataSource,
            tableName,
            Cluster.Client.ServiceProvider.GetRequiredService<Serializer>());

    public override IEdictTableStoreFactory TableStoreFactory =>
        new PostgresTableWriteStoreFactory(
            _dataSource,
            Cluster.Client.ServiceProvider.GetRequiredService<Serializer>());

    // IEdictClaimCheckStore is internal, so this is private-protected — the
    // claim-check subclass that reads it is derived and in this assembly.
    private protected IEdictClaimCheckStore ClaimCheckStore { get; private set; } = null!;

    public string DeadLetterTableName { get; private set; } = "edict_dead_letter";

    public string ClaimCheckTableName { get; private set; } = "edict_claim_check";

    // Per-shape knobs — a subclass overrides only the ones it needs.
    protected virtual Action<EdictOptions>? ConfigureOptions => null;

    protected virtual bool ReplacePublishExecutorWithControllable => false;

    protected virtual bool DecorateGrainStorage => false;

    protected virtual int? ClaimCheckThresholdBytes => null;

    // A schedule fixture returns a FakeTimeProvider here so its scenarios can push
    // a schedule past-due on a virtual clock; every other fixture leaves it null
    // and runs on TimeProvider.System.
    protected virtual TimeProvider? ClockOverride => null;

    public override async Task InitializeAsync()
    {
        var adminConnectionString = await PostgresAssemblyHost.GetAdminConnectionStringAsync();

        var databaseName = $"edict_{Guid.NewGuid():N}";
        _connectionString = await PostgresDatabaseFactory.CreateDatabaseAsync(adminConnectionString, databaseName);
        _dataSource = new NpgsqlDataSourceBuilder(_connectionString).Build();

        var context = new PostgresPersistenceContext(
            _connectionString,
            DeadLetterTableName,
            ClaimCheckTableName,
            ConfigureOptions,
            ReplacePublishExecutorWithControllable,
            DecorateGrainStorage,
            ClaimCheckThresholdBytes,
            OutboxFault,
            StorageFault,
            ClockOverride);
        _contextKey = PostgresPersistenceContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Properties[PostgresPersistenceContextRegistry.ContextKeyProperty] = _contextKey;
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();

        // The persistence wiring bootstraps edict_claim_check during deploy, so
        // the IClaimCheckStoreFixture seam store is constructed here against the
        // same data source.
        ClaimCheckStore = new PostgresClaimCheckStore(_dataSource, ClaimCheckTableName);

        await WarmReminderServiceAsync();
    }

    // LocalReminderService caps its sanity-check wait at a hardcoded 20 s
    // InitialReadMaxWaitTimeForUpdates. On a slow runner the initial table read
    // against a cold Postgres testcontainer can slip past that, and the first
    // command that registers a drain reminder throws OrleansException("Reminder
    // Service is still initializing…"). A warm-up send absorbs that window.
    async Task WarmReminderServiceAsync()
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await Sender.SendAsync(new IncrementCounterCommand(Guid.NewGuid()));
                return;
            }
            catch (OrleansException orleansException) when (orleansException.Message.Contains("still initializing", StringComparison.Ordinal))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
        }
        throw new TimeoutException("ReminderService warm-up never settled within the 2-minute deadline.");
    }

    public override async Task DisposeAsync()
    {
        if (Cluster is not null)
        {
            await Cluster.DisposeAsync();
        }
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }
        PostgresPersistenceContextRegistry.Unregister(_contextKey);
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(CounterAggregate).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(Orleans.Hosting.ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[PostgresPersistenceContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing from silo configuration.");
            var ctx = PostgresPersistenceContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            // A cold Postgres testcontainer's first DDL bootstrap and burst of
            // cold grain activations can run past Orleans' default 30 s response
            // timeout on a slow runner.
            siloBuilder.Configure<SiloMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromMinutes(2));
            siloBuilder.Services.AddSingleton(ctx.ClockOverride ?? TimeProvider.System);
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();

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

            // The dumb deliver-once reference stream: MemoryStreams delivers each
            // publish once to its implicit subscribers, carries the EventId
            // intact, and never redelivers — the persistence axis never asserts a
            // streaming property of it.
            siloBuilder.AddMemoryStreams("edict");

            // Real Postgres persistence through the consumer-facing extension:
            // grain storage, AdoNet PubSubStore + reminders, the dead-letter
            // table repository, the claim-check store, and the table-store
            // factory all land from this one call.
            siloBuilder.AddEdictPostgresPersistence(persistenceOptions =>
            {
                persistenceOptions.ConnectionString = ctx.ConnectionString;
                persistenceOptions.DeadLetterTableName = ctx.DeadLetterTableName;
                persistenceOptions.ClaimCheckTableName = ctx.ClaimCheckTableName;
            });

            if (ctx.ClaimCheckThresholdBytes is int thresholdBytes)
            {
                // A low threshold forces every raised event onto the pointer
                // branch, exercising the receiver-unwrap path on the real
                // Postgres claim-check store. Registered after persistence binds
                // so the resolved store is the Postgres one.
                siloBuilder.Services.AddSingleton(serviceProvider => new ClaimCheckPolicy(
                    serviceProvider.GetRequiredService<Serializer>(),
                    thresholdBytes: thresholdBytes,
                    store: serviceProvider.GetRequiredService<IEdictClaimCheckStore>(),
                    accessors: serviceProvider.GetRequiredService<IEventStreamAccessors>()));
            }

            if (ctx.DecorateGrainStorage)
            {
                ControllableGrainStorage.Decorate(siloBuilder.Services, ctx.StorageFault);
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
        }
    }
}
