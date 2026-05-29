using System.Reflection;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Commands;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.ClaimCheck;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.EventHandler;
using Edict.Core.Metrics;
using Edict.Core.Outbox;
using Edict.Core.Sagas;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Testing.Internal;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Configuration;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Testing;

/// <summary>
/// The shipped in-memory Test Framework entry point. Boots the
/// consumer's grains on an in-memory Orleans cluster with Edict auto-wired and
/// runs the <em>real</em> Outbox/saga engine over memory streams, an in-memory
/// single store and a virtual <see cref="TimeProvider"/> — so consumer code
/// behaves identically under test and in production. A whole workflow is
/// asserted with one <c>await Verify(app.Timeline)</c>. Traces are not
/// captured.
/// </summary>
public sealed class EdictTestApp : IAsyncDisposable
{
    readonly TestCluster _cluster;
    readonly HarnessContext _context;

    EdictTestApp(TestCluster cluster, HarnessContext context)
    {
        _cluster = cluster;
        _context = context;
    }

    /// <summary>The single Verify-shaped view of everything the workflow did.</summary>
    public Timeline Timeline => _context.Recorder.Snapshot();

    public static async Task<EdictTestApp> StartAsync(Action<EdictTestAppBuilder> configure)
    {
        var builder = new EdictTestAppBuilder();
        configure(builder);

        var context = new HarnessContext(
            builder.ConsumerAssembly,
            new TimelineRecorder(),
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new InMemoryTableStoreFactory(),
            SubscriberMap.Build(builder.ConsumerAssembly),
            ChaosOptions.Default,
            new InMemoryClaimCheckStore(),
            EdictTestAppBuilder.DefaultClaimCheckThresholdBytes,
            builder.Replacements);

        TestCluster cluster;
        // The id flows down the async flow into the Orleans-instantiated
        // configurators, which resolve this context from the registry.
        using (HarnessRegistry.Activate(Guid.NewGuid().ToString("N"), context))
        {
            var clusterBuilder = new TestClusterBuilder(1);
            clusterBuilder.AddSiloBuilderConfigurator<SiloConfigurator>();
            clusterBuilder.AddClientBuilderConfigurator<ClientConfigurator>();
            cluster = clusterBuilder.Build();
            await cluster.DeployAsync();
        }

        return new EdictTestApp(cluster, context);
    }

    /// <summary>Issues a Command through the real <see cref="IEdictSender"/>.</summary>
    public Task<EdictCommandResult> Send(EdictCommand command) =>
        _cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>().Send(command);

    /// <summary>
    /// Typed probe over <see cref="IEdictSaga.GetEdictProgressAsync"/>: returns
    /// the saga grain's durable <c>Progress</c> for direct Verify-snapshot
    /// assertion. Tests pass the saga implementation class plus its progress
    /// type — e.g.
    /// <c>app.GetSagaProgress&lt;OrderPaymentSaga, OrderPaymentProgress&gt;(orderId)</c>.
    /// <para>
    /// The probe goes through the hand-written <see cref="IEdictSaga"/>
    /// interface plus a class-name prefix, not the generator-emitted
    /// <c>I{Saga}</c>, because Orleans's codegen runs before Edict's generator
    /// and so does not produce a client proxy for the generator-emitted
    /// interface.
    /// </para>
    /// </summary>
    public async Task<TProgress> GetSagaProgress<TSaga, TProgress>(Guid key)
        where TSaga : EdictSaga<TProgress>
        where TProgress : Edict.Contracts.Persistence.IEdictPersistedState, new()
    {
        var grain = _cluster.GrainFactory.GetGrain<IEdictSaga>(key, typeof(TSaga).FullName);
        return (TProgress)await grain.GetEdictProgressAsync();
    }

    /// <summary>
    /// Per-grain-type probe over the silo-local metrics cache: returns the
    /// aggregate outbox state the
    /// <c>edict.outbox.pending.count</c> +
    /// <c>edict.outbox.oldest_entry.age</c> observable gauges would read at
    /// scrape time. <c>TotalPending</c> is the sum across every live grain of
    /// <paramref name="grainType"/> on this silo; <c>OldestEnqueuedAt</c> is
    /// the earliest enqueue timestamp across those grains (null when no entry
    /// of that type has any pending work). Tests assert on this when they need
    /// to verify outbox state shape without attaching a MeterListener.
    /// </summary>
    public (int TotalPending, DateTimeOffset? OldestEnqueuedAt) GetOutboxState(string grainType)
    {
        var cache = _context.MetricsCache
            ?? throw new InvalidOperationException(
                "Silo metrics cache has not been constructed yet. Send at least one command first.");
        return cache.GetOutboxState(grainType);
    }

    /// <summary>
    /// Per-saga-type probe over the silo-local metrics cache: returns the
    /// most-recent <c>lastHandledAt</c> across every live saga of
    /// <paramref name="sagaType"/> on this silo, or <c>null</c> when no saga
    /// of that type has handled an event. Pair with
    /// <see cref="AdvanceClock"/> in tests to verify
    /// <c>edict.saga.progress.age</c> grows when a saga sits idle.
    /// </summary>
    public DateTimeOffset? GetSagaState(string sagaType)
    {
        var cache = _context.MetricsCache
            ?? throw new InvalidOperationException(
                "Silo metrics cache has not been constructed yet. Send at least one command first.");
        return cache.GetSagaState(sagaType);
    }

    /// <summary>
    /// Typed probe over the in-memory table store: returns the projection row a
    /// <see cref="EdictTableProjectionBuilder{T}"/> last wrote for the supplied
    /// <c>(tableName, partitionKey, rowKey)</c>, or <c>null</c> when the
    /// projection's <c>Handle</c> never ran for this key.
    /// </summary>
    public async Task<TRow?> GetProjectionRow<TRow>(string tableName, string partitionKey, string rowKey)
        where TRow : class, new()
    {
        var store = await _context.TableStoreFactory.CreateAsync<TRow>(tableName);
        return await store.GetAsync(partitionKey, rowKey);
    }

    /// <summary>
    /// Waits for the in-memory engine to quiesce: the inline outbox drain plus
    /// the asynchronous memory-stream fan-out to projection builders and sagas
    /// (whose own dispatched Commands cascade). Settles on a stable timeline,
    /// not a fixed delay, so it is as fast as the cluster allows.
    /// </summary>
    public async Task Drain()
    {
        // Custom in-proc sync stream provider dispatches on publish; nothing
        // truly asynchronous is left in the event hop, so the drain just polls
        // for the in-silo SendCommand fan-out cascade to settle. On every
        // stability window we flush the chaos-held queue and, if it released
        // anything, re-poll until the cascade settles again — release is the
        // load-bearing trigger, never a wall-clock wait.
        var stableWindow = TimeSpan.FromMilliseconds(250);
        var timeout = TimeSpan.FromSeconds(10);
        var start = DateTime.UtcNow;
        var lastCount = -1;
        var lastChange = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            var count = _context.Recorder.Count;
            var inflight = _context.PublishExecutor?.OutstandingDispatches ?? 0;
            if (count != lastCount || inflight > 0)
            {
                lastCount = count;
                lastChange = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - lastChange >= stableWindow)
            {
                var flushed = _context.PublishExecutor is { } exec
                    ? await exec.FlushHeldAsync()
                    : 0;
                if (flushed == 0)
                {
                    return;
                }
                // Released events trigger consumer invocations + cascades:
                // reset the stability gate and keep polling.
                lastChange = DateTime.UtcNow;
            }
            await Task.Delay(25);
        }
    }

    /// <summary>
    /// Advances the virtual clock so backoff/dead-letter timing elapses with no
    /// real wait, then drains. The clock is the seam the engine reads for
    /// backoff gating.
    /// </summary>
    public async Task AdvanceClock(TimeSpan by)
    {
        _context.Clock.Advance(by);
        await Drain();
    }

    public async ValueTask DisposeAsync() => await _cluster.DisposeAsync();

    static void ConfigureSerialization(HarnessContext ctx, IServiceCollection services) =>
        services.AddSerializer(s => s
            .AddAssembly(ctx.ConsumerAssembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer());

    // Scan AppDomain so both the consumer's handler assembly AND its referenced
    // contracts assembly (events live there) contribute to the route map and
    // the event-stream accessor map. Passing only the handler assembly would
    // miss every event whose [EdictStream] annotation lives next to the
    // contract type, not next to the handler.
    static void InvokeAddEdict(IServiceCollection services) =>
        services.AddEdict();

    // Plug the in-memory IEdictTableRepository<EdictDeadLetterEntry> behind
    // AddEdict()'s auto-registered IEdictDeadLetterRepository facade so silo
    // (write) and client (read) share one backing dictionary.
    static void RegisterInMemoryDeadLetterTable(IServiceCollection services, HarnessContext ctx) =>
        services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(_ =>
            (IEdictTableRepository<EdictDeadLetterEntry>)
                ctx.TableStoreFactory
                    .CreateAsync<EdictDeadLetterEntry>(EdictDeadLetterProjectionBuilder.DeadLetterPartition)
                    .GetAwaiter().GetResult());

    // Re-point IEdictSender at the recording decorator wrapping the real sender,
    // so a saga's in-silo dispatched Command and a test's client Command share
    // one timeline. Last AddSingleton wins in MS DI.
    static void DecorateSender(IServiceCollection services, TimelineRecorder recorder) =>
        services.AddSingleton<IEdictSender>(serviceProvider =>
            new RecordingSender(ActivatorUtilities.CreateInstance<EdictSender>(serviceProvider), recorder));

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var ctx = HarnessRegistry.Current;

            siloBuilder.AddActivityPropagation();
            ConfigureSerialization(ctx, siloBuilder.Services);

            // Register the virtual clock before AddEdictOutbox so its
            // TryAddSingleton(TimeProvider.System) is a no-op (the engine
            // reads this seam for backoff gating).
            siloBuilder.Services.AddSingleton<TimeProvider>(ctx.Clock);
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(ctx.TableStoreFactory);
            siloBuilder.Services.AddSingleton<IEdictClaimCheckStore>(ctx.ClaimCheckStore);
            siloBuilder.Services.AddSingleton(serviceProvider => new ClaimCheckPolicy(
                serviceProvider.GetRequiredService<Serializer>(),
                ctx.ClaimCheckThresholdBytes,
                serviceProvider.GetRequiredService<IEdictClaimCheckStore>(),
                serviceProvider.GetRequiredService<IEventStreamAccessors>()));

            InvokeAddEdict(siloBuilder.Services);
            RegisterInMemoryDeadLetterTable(siloBuilder.Services, ctx);
            siloBuilder.Services.AddEdictOutbox();

            // Replace AddEdict()'s default IEdictMetricsCache with a
            // harness-shared instance so the probe methods on EdictTestApp
            // read the same cache the silo's OutboxHost + EdictSaga push to.
            // Constructed eagerly (rather than via TryAddSingleton + lazy DI
            // resolution) so the static gauges register before the first grain
            // activates and the conformance scenario's MeterListener attaches.
            var harnessCache = new EdictMetricsCache(ctx.Clock);
            ctx.MetricsCache = harnessCache;
            siloBuilder.Services.AddSingleton<IEdictMetricsCache>(harnessCache);

            // Swap the bare PublishEvent executor for the in-process dispatcher
            // (the single Event choke point — records and fan-outs in one).
            // The drain engine indexes executors by Kind, so the original must
            // be removed, not added to.
            var original = siloBuilder.Services.Single(d =>
                d.ServiceType == typeof(IOutboxEffectExecutor)
                && d.ImplementationType == typeof(PublishEventExecutor));
            siloBuilder.Services.Remove(original);
            siloBuilder.Services.AddSingleton(ctx.SubscriberMap);
            siloBuilder.Services.AddSingleton(ctx.Chaos);
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor>(serviceProvider =>
            {
                var inst = ActivatorUtilities.CreateInstance<InProcPublishExecutor>(serviceProvider, ctx.Recorder);
                ctx.PublishExecutor = inst;
                return inst;
            });

            // Swap the bare InvokeHandler executor for the recording variant so
            // EdictEventHandler invocations surface on the timeline.
            var originalInvoke = siloBuilder.Services.Single(d =>
                d.ServiceType == typeof(IOutboxEffectExecutor)
                && d.ImplementationType == typeof(InvokeHandlerExecutor));
            siloBuilder.Services.Remove(originalInvoke);
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor>(serviceProvider =>
                ActivatorUtilities.CreateInstance<InProcInvokeHandlerExecutor>(serviceProvider, ctx.Recorder));

            DecorateSender(siloBuilder.Services, ctx.Recorder);

            // Builder-supplied fakes win over every harness/AddEdict
            // registration above — MS DI resolves the last AddSingleton, so the
            // replacements run last.
            foreach (var apply in ctx.Replacements)
            {
                apply(siloBuilder.Services);
            }

            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryGrainStorage("edict-state");
            // Memory streams are registered because EdictIdempotencyBase's
            // OutboxHost asks for one via the silo's "edict" stream provider,
            // but the in-process dispatcher bypasses it — no event is ever
            // pushed to a memory queue.
            siloBuilder.AddMemoryStreams("edict");
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(
            IConfiguration configuration,
            IClientBuilder clientBuilder)
        {
            var ctx = HarnessRegistry.Current;
            clientBuilder.AddActivityPropagation();
            ConfigureSerialization(ctx, clientBuilder.Services);
            InvokeAddEdict(clientBuilder.Services);
            RegisterInMemoryDeadLetterTable(clientBuilder.Services, ctx);
            DecorateSender(clientBuilder.Services, ctx.Recorder);

            foreach (var apply in ctx.Replacements)
            {
                apply(clientBuilder.Services);
            }
        }
    }
}
