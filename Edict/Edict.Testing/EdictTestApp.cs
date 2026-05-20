using System.Reflection;

using Edict.Contracts.Commands;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.Saga;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Testing.Hosting;
using Edict.Testing.InProcess;
using Edict.Testing.Recording;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Testing;

/// <summary>
/// The shipped in-memory Test Framework entry point (ADR 0016). Boots the
/// consumer's grains on an in-memory Orleans cluster with Edict auto-wired and
/// runs the <em>real</em> Outbox/saga engine over memory streams, an in-memory
/// single store and a virtual <see cref="TimeProvider"/> — so consumer code
/// behaves identically under test and in production. A whole workflow is
/// asserted with one <c>await Verify(app.Timeline)</c>. Traces are not
/// captured (ADR 0016).
/// </summary>
public sealed class EdictTestApp : IAsyncDisposable
{
    readonly TestCluster _cluster;
    readonly EdictTestHarnessContext _context;

    EdictTestApp(TestCluster cluster, EdictTestHarnessContext context)
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

        var context = new EdictTestHarnessContext(
            builder.ConsumerAssembly,
            new EdictTimelineRecorder(),
            new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new InMemoryEdictTableStoreFactory(),
            InProcImplicitSubscriberMap.Build(builder.ConsumerAssembly),
            builder.Chaos);

        TestCluster cluster;
        // The id flows down the async flow into the Orleans-instantiated
        // configurators, which resolve this context from the registry.
        using (EdictTestHarnessRegistry.Activate(Guid.NewGuid().ToString("N"), context))
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
    /// assertion (the state-first assertion pivot captured in #53). Tests pass
    /// the saga implementation class plus its progress type — e.g.
    /// <c>app.GetSagaProgress&lt;OrderPaymentSaga, OrderPaymentProgress&gt;(orderId)</c>.
    /// <para>
    /// The probe goes through the hand-written <see cref="IEdictSaga"/>
    /// interface plus a class-name prefix, not the generator-emitted
    /// <c>I{Saga}</c>, because Orleans's codegen runs before Edict's generator
    /// and so does not produce a client proxy for the generator-emitted
    /// interface (ADR 0006). Routing via the hand-written brand interface keeps
    /// the typed test surface without fighting that ordering.
    /// </para>
    /// </summary>
    public async Task<TProgress> GetSagaProgress<TSaga, TProgress>(Guid key)
        where TSaga : EdictSaga<TProgress>
        where TProgress : new()
    {
        var grain = _cluster.GrainFactory.GetGrain<IEdictSaga>(key, typeof(TSaga).FullName);
        return (TProgress)await grain.GetEdictProgressAsync();
    }

    /// <summary>
    /// Typed probe over the in-memory table store: returns the projection row a
    /// <see cref="EdictTableProjectionBuilder{T}"/> last wrote for the supplied
    /// <c>(tableName, partitionKey, rowKey)</c>, or <c>null</c> when the
    /// projection's <c>Handle</c> never ran for this key. Tests Verify the row
    /// directly — the same load-apply-writeback shape ADR 0012 guarantees in
    /// production, but reading the in-memory store instead of Azure Table.
    /// </summary>
    public async Task<TRow?> GetProjectionRow<TRow>(string tableName, string partitionKey, string rowKey)
        where TRow : class, new()
    {
        // The factory caches one store per (tableName, T); the upsert path the
        // engine drained writes through the SAME store, so reading via the
        // typed lookup is a coherent point-in-time read.
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
        // The window must exceed the worst-case single memory-stream hop
        // (publish → pulling agent → consumer Handle → next dispatch), incl.
        // first-activation latency, so a saga cascade is never cut short.
        // Custom in-proc sync stream provider dispatches on publish; nothing
        // truly asynchronous is left in the event hop, so the drain just polls
        // for the in-silo SendCommand fan-out cascade to settle.
        var stableWindow = TimeSpan.FromMilliseconds(250);
        var timeout = TimeSpan.FromSeconds(10);
        var start = DateTime.UtcNow;
        var lastCount = -1;
        var lastChange = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            var count = _context.Recorder.Count;
            if (count != lastCount)
            {
                lastCount = count;
                lastChange = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - lastChange >= stableWindow)
            {
                return;
            }
            await Task.Delay(25);
        }
    }

    /// <summary>
    /// Advances the virtual clock so backoff/dead-letter timing elapses with no
    /// real wait, then drains. The clock is the ADR 0018/0019 seam the engine
    /// reads for backoff gating.
    /// </summary>
    public async Task AdvanceClock(TimeSpan by)
    {
        _context.Clock.Advance(by);
        await Drain();
    }

    public async ValueTask DisposeAsync() => await _cluster.DisposeAsync();

    static void ConfigureSerialization(EdictTestHarnessContext ctx, IServiceCollection services) =>
        services.AddSerializer(s => s
            .AddAssembly(ctx.ConsumerAssembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer());

    // Drives the hand-authored AddEdict() with the explicit-assemblies overload
    // so EdictTestApp routes are sourced from the consumer assembly alone
    // (ADR 0021 — deterministic for test contexts; the AppDomain-scan happy-path
    // is the consumer-app entry only).
    static void InvokeAddEdict(IServiceCollection services, Assembly consumerAssembly) =>
        services.AddEdict(consumerAssembly);

    // Plug the in-memory IEdictTableRepository<EdictDeadLetterEntry> behind
    // AddEdict()'s auto-registered IEdictDeadLetterRepository facade (ADR 0022).
    // The store is held on the harness context, so silo (write) and client
    // (read) share one backing dictionary — the test can call
    // IEdictDeadLetterRepository.ListAllAsync() on the client and see the row
    // the engine wrote on the silo.
    static void RegisterInMemoryDeadLetterTable(IServiceCollection services, EdictTestHarnessContext ctx) =>
        services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(_ =>
            (IEdictTableRepository<EdictDeadLetterEntry>)
                ctx.TableStoreFactory
                    .CreateAsync<EdictDeadLetterEntry>(EdictDeadLetterProjectionBuilder.DeadLetterPartition)
                    .GetAwaiter().GetResult());

    // Re-point IEdictSender at the recording decorator wrapping the real sender,
    // so a saga's in-silo dispatched Command and a test's client Command share
    // one timeline. Last AddSingleton wins in MS DI.
    static void DecorateSender(IServiceCollection services, EdictTimelineRecorder recorder) =>
        services.AddSingleton<IEdictSender>(sp =>
            new RecordingEdictSender(ActivatorUtilities.CreateInstance<EdictSender>(sp), recorder));

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var ctx = EdictTestHarnessRegistry.Current;

            siloBuilder.AddActivityPropagation();
            ConfigureSerialization(ctx, siloBuilder.Services);

            // Register the virtual clock before AddEdictOutbox so its
            // TryAddSingleton(TimeProvider.System) is a no-op (ADR 0018 seam).
            siloBuilder.Services.AddSingleton<TimeProvider>(ctx.Clock);
            siloBuilder.Services.AddSingleton<IEdictTableStoreFactory>(ctx.TableStoreFactory);

            InvokeAddEdict(siloBuilder.Services, ctx.ConsumerAssembly);
            RegisterInMemoryDeadLetterTable(siloBuilder.Services, ctx);
            siloBuilder.Services.AddEdictOutbox();

            // Swap the bare PublishEvent executor for the in-process dispatcher
            // (the single Event choke point — records and fan-outs in one). The
            // drain engine indexes executors by Kind, so the original must be
            // removed, not added to.
            var original = siloBuilder.Services.Single(d =>
                d.ServiceType == typeof(IOutboxEffectExecutor)
                && d.ImplementationType == typeof(PublishEventExecutor));
            siloBuilder.Services.Remove(original);
            siloBuilder.Services.AddSingleton(ctx.SubscriberMap);
            siloBuilder.Services.AddSingleton(ctx.Chaos);
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor>(sp =>
                ActivatorUtilities.CreateInstance<InProcPublishEventExecutor>(sp, ctx.Recorder));

            DecorateSender(siloBuilder.Services, ctx.Recorder);

            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryGrainStorage("edict-state");
            // Memory streams are still registered because
            // EdictIdempotencyBase's IOutboxHost.StreamProvider asks for one,
            // but the in-process dispatcher bypasses it — no event is ever
            // pushed to a memory queue, so the pulling-agent that fails for
            // referenced-assembly consumers in #53 is out of the loop.
            siloBuilder.AddMemoryStreams("edict");
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            IClientBuilder clientBuilder)
        {
            var ctx = EdictTestHarnessRegistry.Current;
            clientBuilder.AddActivityPropagation();
            ConfigureSerialization(ctx, clientBuilder.Services);
            InvokeAddEdict(clientBuilder.Services, ctx.ConsumerAssembly);
            RegisterInMemoryDeadLetterTable(clientBuilder.Services, ctx);
            DecorateSender(clientBuilder.Services, ctx.Recorder);
        }
    }
}

