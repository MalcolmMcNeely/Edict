using System.Reflection;

using Edict.Contracts.Audit;
using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Commands;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Projections;
using Edict.Contracts.Routing;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Contracts.Tenancy;
using Edict.Core;
using Edict.Core.Audit;
using Edict.Core.ClaimCheck;
using Edict.Core.Commands;
using Edict.Core.DeadLetter;
using Edict.Core.EventHandler;
using Edict.Core.Metrics;
using Edict.Core.Outbox;
using Edict.Core.Projections;
using Edict.Core.Sagas;
using Edict.Core.Schedules;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Core.Tenancy;
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

    /// <summary>
    /// The actor an audited send is attributed to when a test never calls
    /// <see cref="ActAs"/>, so turning auditing on with a bare
    /// <see cref="EdictTestAppBuilder.WithAudit"/> does not trip the origin
    /// fail-closed.
    /// </summary>
    public static readonly EdictPrincipal DefaultPrincipal = EdictPrincipal.Of("edict-test-principal");

    /// <summary>The single Verify-shaped view of everything the workflow did.</summary>
    public Timeline Timeline => _context.Recorder.Snapshot();

    /// <summary>
    /// The consumer read surface over the in-memory audit log: query the captured
    /// chain (<see cref="IEdictAuditRepository.ByEntityAsync(string, string, EdictTenantId?, CancellationToken)"/>,
    /// <c>ByCorrelationAsync</c>, <c>ByPrincipalAsync</c>), verify it is unaltered
    /// (<see cref="IEdictAuditRepository.VerifyEntityChainAsync"/>), and retrieve a
    /// captured body as bytes (<see cref="IEdictAuditRepository.GetPayloadAsync"/>)
    /// or as the typed message (<see cref="IEdictAuditRepository.GetMessageAsync"/>).
    /// Available only when the app was started with
    /// <see cref="EdictTestAppBuilder.WithAudit"/>.
    /// </summary>
    public IEdictAuditRepository Audit =>
        _context.AuditEnabled
            ? new EdictDefaultAuditRepository(
                _context.AuditStore,
                _context.PayloadStore,
                _cluster.Client.ServiceProvider.GetRequiredService<Serializer>())
            : throw new InvalidOperationException(
                "Auditing is off. Call WithAudit() on the EdictTestApp builder to capture and assert audit records.");

    /// <summary>
    /// The ambient-tenant-scoped read surface over the in-memory audit log: the same
    /// queries as <see cref="Audit"/>, each filtered to the tenant
    /// <see cref="RunAsTenant"/> set, so a business sees only its own trail and neither
    /// another tenant's records nor the public ones. A read with no
    /// <see cref="RunAsTenant"/> fails closed rather than answering under the wrong wall.
    /// Available only when the app was started with both
    /// <see cref="EdictTestAppBuilder.WithAudit"/> and
    /// <see cref="EdictTestAppBuilder.WithTenancy"/>.
    /// </summary>
    public IEdictTenantScopedAuditRepository TenantAudit
    {
        get
        {
            if (!_context.AuditEnabled)
            {
                throw new InvalidOperationException(
                    "Auditing is off. Call WithAudit() on the EdictTestApp builder to capture and assert audit records.");
            }
            if (!_context.TenancyEnabled)
            {
                throw new InvalidOperationException(
                    "Tenancy is off. Call WithTenancy() on the EdictTestApp builder before TenantAudit can scope reads to a tenant.");
            }

            var serializer = _cluster.Client.ServiceProvider.GetRequiredService<Serializer>();
            var operatorRepository = new EdictDefaultAuditRepository(_context.AuditStore, _context.PayloadStore, serializer);
            var resolver = new DelegateTenantResolver(_cluster.Client.ServiceProvider, _ => _context.CurrentTenant);
            return new EdictTenantScopedAuditRepository(operatorRepository, resolver);
        }
    }

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
            builder.ChaosDisabled ? ChaosOptions.None : ChaosOptions.Default,
            new InMemoryClaimCheckStore(),
            EdictTestAppBuilder.DefaultClaimCheckThresholdBytes,
            builder.AuditEnabled,
            builder.TenancyEnabled,
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
    public Task<EdictCommandResult> SendAsync(EdictCommand command) =>
        _cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>().SendAsync(command);

    /// <summary>
    /// Issues a Command through the explicit establishing-crossing overload: stamps
    /// <paramref name="tenant"/> onto <paramref name="command"/> directly, the public-to-tenant
    /// send that mints a wall without an ambient resolver, so a test can seed a company's
    /// data under its own tenant or model the "register your company" onboarding. The
    /// crossing is authorized by construction; <see cref="RunAsTenant"/> then reads it back.
    /// Available only when the app was started with
    /// <see cref="EdictTestAppBuilder.WithTenancy"/>.
    /// </summary>
    public Task<EdictCommandResult> SendAsync(EdictCommand command, EdictTenantId tenant)
    {
        if (!_context.TenancyEnabled)
        {
            throw new InvalidOperationException(
                "Tenancy is off. Call WithTenancy() on the EdictTestApp builder before an establishing crossing can stamp a tenant.");
        }

        return _cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>().SendAsync(command, tenant);
    }

    /// <summary>
    /// Attributes every subsequent audited send to <paramref name="principal"/>, the
    /// actor the audit edge resolver yields at the origin. Call it before
    /// <see cref="SendAsync"/> to record who did what; absent any call, sends carry
    /// <see cref="DefaultPrincipal"/>. Available only when the app was started with
    /// <see cref="EdictTestAppBuilder.WithAudit"/>.
    /// </summary>
    public void ActAs(EdictPrincipal principal)
    {
        if (!_context.AuditEnabled)
        {
            throw new InvalidOperationException(
                "Auditing is off. Call WithAudit() on the EdictTestApp builder before ActAs() can set the audited principal.");
        }

        _context.CurrentPrincipal = principal;
    }

    /// <summary>
    /// Acts as <paramref name="tenant"/>: scopes every subsequent ambient send and read to
    /// that tenant's wall, the value the tenant edge resolver yields at the origin. Call it
    /// before <see cref="QueryMyTenantPartitionAsync"/> or <see cref="TenantAudit"/> to read
    /// "my own" partition, and before a bare <see cref="SendAsync(EdictCommand)"/> of a
    /// tenant-scoped command to attribute it. Switching the tenant swaps the visible
    /// directory and audit trail; a tenant that owns no rows reads empty by construction.
    /// Absent any call the resolver yields null and a tenant-scoped read fails closed.
    /// Available only when the app was started with <see cref="EdictTestAppBuilder.WithTenancy"/>.
    /// </summary>
    public void RunAsTenant(EdictTenantId tenant)
    {
        if (!_context.TenancyEnabled)
        {
            throw new InvalidOperationException(
                "Tenancy is off. Call WithTenancy() on the EdictTestApp builder before RunAsTenant() can set the ambient tenant.");
        }

        _context.CurrentTenant = tenant;
    }

    /// <summary>
    /// Rewrites a stored audit record in place, the one mutation a production WORM
    /// store refuses, so a test can prove
    /// <see cref="IEdictAuditRepository.VerifyEntityChainAsync"/> catches an altered
    /// chain. Pass a record read back from <see cref="Audit"/> with a field changed
    /// (e.g. <c>record with { MessageType = "tampered" }</c>); the rewrite keeps the
    /// same <see cref="EdictAuditRecord.RecordId"/> so it lands on the same row, and
    /// the next verification reports it broken at its
    /// <see cref="EdictAuditRecord.Sequence"/>. Available only when the app was
    /// started with <see cref="EdictTestAppBuilder.WithAudit"/>.
    /// </summary>
    public void TamperWithAuditRecord(EdictAuditRecord rewritten)
    {
        ArgumentNullException.ThrowIfNull(rewritten);
        if (!_context.AuditEnabled)
        {
            throw new InvalidOperationException(
                "Auditing is off. Call WithAudit() on the EdictTestApp builder before a record exists to tamper with.");
        }

        _context.AuditStore.Overwrite(rewritten);
    }

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
        var composedKey = EdictKeyComposer.Compose(null, key.ToString("N"));
        var grain = _cluster.GrainFactory.GetGrain<IEdictSaga>(composedKey, typeof(TSaga).FullName);
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
    /// <see cref="EdictListProjectionBuilder{TRow}"/> last wrote for the supplied
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
    /// Typed probe over the in-grain (State) projection reader: returns the read
    /// model an <see cref="EdictProjectionBuilder{TProjection}"/> committed for the
    /// supplied routing <paramref name="key"/>, or <c>null</c> when the projection's
    /// <c>HandleAsync</c> never ran for this key. The read goes through the same
    /// <see cref="IEdictProjectionReader{TProjection}"/> seam the application tier
    /// binds to. Call <see cref="Drain"/> first so the inline write has landed; the
    /// cursorless read then answers immediately.
    /// </summary>
    public async Task<TProjection?> ReadProjectionAsync<TProjection>(Guid key)
        where TProjection : class
    {
        var reader = _cluster.Client.ServiceProvider.GetRequiredService<IEdictProjectionReader<TProjection>>();
        return (await reader.ReadAsync(key)).Value;
    }

    /// <summary>
    /// Reads every row in the caller's own tenant partition of a tenant-scoped List
    /// projection, through the same <see cref="IEdictTenantScopedListProjectionReader{TListProjection}"/>
    /// the application tier binds to. The reader takes no partition key: the framework composes
    /// the tenant <see cref="RunAsTenant"/> set, so the query answers "my own" rows and a
    /// different tenant's identical call returns empty by construction. Call <see cref="Drain"/>
    /// first so the projection write has landed. Available only when the app was started with
    /// <see cref="EdictTestAppBuilder.WithTenancy"/>.
    /// </summary>
    public async Task<IReadOnlyList<TListProjection>> QueryMyTenantPartitionAsync<TListProjection>()
        where TListProjection : class
    {
        if (!_context.TenancyEnabled)
        {
            throw new InvalidOperationException(
                "Tenancy is off. Call WithTenancy() on the EdictTestApp builder before a tenant-scoped partition read.");
        }

        var reader = _cluster.Client.ServiceProvider.GetRequiredService<IEdictTenantScopedListProjectionReader<TListProjection>>();
        return (await reader.QueryMyPartitionAsync()).Rows;
    }

    /// <summary>
    /// Waits for the in-memory engine to quiesce: the inline outbox drain plus
    /// the asynchronous memory-stream fan-out to projection builders and sagas
    /// (whose own dispatched Commands cascade). Quiet requires the dispatch
    /// counter, held queue, silo-wide outbox pending aggregate AND a short
    /// recorder-count stability window — the counter+pending pair catches the
    /// cascade race the recorder alone misses, the stability window catches
    /// the gap between FakeTimeProvider firing a grain timer and the resulting
    /// grain method landing on the scheduler.
    /// </summary>
    public Task Drain() => Drain(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500));

    // The 30s timeout and 500ms stability window are tuned for slow CI runners
    // and are the shipped values; this overload exists so a test that drives a
    // deliberately non-settling workflow can assert the timeout path in seconds.
    internal async Task Drain(TimeSpan timeout, TimeSpan stableWindow)
    {
        var start = DateTime.UtcNow;
        var executor = _context.PublishExecutor;
        var cache = _context.MetricsCache;
        var lastCount = -1;
        var lastChange = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            executor?.FirstFault?.Throw();

            var inflight = executor?.OutstandingDispatches ?? 0;
            var held = executor?.HeldCount ?? 0;
            var pending = cache?.GetOutboxStateAggregate().TotalPending ?? 0;
            var count = _context.Recorder.Count;

            if (count != lastCount)
            {
                lastCount = count;
                lastChange = DateTime.UtcNow;
            }

            if (inflight == 0 && held == 0 && pending == 0
                && DateTime.UtcNow - lastChange >= stableWindow)
            {
                // A fault captured as the final dispatch decremented the counter
                // is published-before that decrement, so it is visible now the
                // counter reads zero: surface it rather than returning clean.
                executor?.FirstFault?.Throw();
                return;
            }

            if (inflight == 0 && held > 0)
            {
                // Held events release on arrivals to the same subscriber.
                // No arrivals are coming, so release them explicitly.
                await executor!.FlushHeldAsync();
                lastChange = DateTime.UtcNow;
            }

            await Task.Delay(10);
        }

        executor?.FirstFault?.Throw();

        var outstanding = executor?.OutstandingDispatches ?? 0;
        var heldDepth = executor?.HeldCount ?? 0;
        var pendingEntries = cache?.GetOutboxStateAggregate().TotalPending ?? 0;
        var timelineEntries = _context.Recorder.Count;
        throw new TimeoutException(
            $"Drain did not settle within {timeout.TotalSeconds:0}s: outstanding dispatches {outstanding}, held queue depth {heldDepth}, " +
            $"pending outbox entries {pendingEntries}, timeline entries {timelineEntries}. A HandleAsync that never returns, or an effect " +
            "that never drains (e.g. a backoff retry awaiting an AdvanceClock the test never calls), leaves the engine non-quiescent. " +
            EdictChaos.ReproduceInstruction);
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

    /// <summary>
    /// Drives the next round of due schedule fires deterministically, without
    /// hardcoding the cadence. Reads the soonest due instant across every grain a
    /// Command has been routed to, advances the virtual clock to it, fires every
    /// grain that is now due, and drains so the fired outcome (raised events,
    /// dispatched Commands) lands on the <see cref="Timeline"/>. A no-op when no
    /// schedule is active. Chainable: call it once per fire to walk a multi-step
    /// scheduled workflow.
    /// </summary>
    public async Task FireDueSchedulesAsync()
    {
        var fireables = _context.RoutedGrains.Keys
            .Select(routed => _cluster.GrainFactory.GetGrain<IEdictScheduleFireable>(routed.Key, routed.GrainClassName))
            .ToArray();

        DateTimeOffset? soonest = null;
        var dueByGrain = new List<(IEdictScheduleFireable Grain, DateTimeOffset? Due)>(fireables.Length);
        foreach (var fireable in fireables)
        {
            var due = await fireable.PeekSoonestScheduleDueAsync();
            dueByGrain.Add((fireable, due));
            if (due is { } instant && (soonest is null || instant < soonest))
            {
                soonest = instant;
            }
        }

        if (soonest is null)
        {
            return;
        }

        var now = _context.Clock.GetUtcNow();
        if (soonest.Value > now)
        {
            _context.Clock.Advance(soonest.Value - now);
        }

        var fireInstant = _context.Clock.GetUtcNow();
        foreach (var (grain, due) in dueByGrain)
        {
            if (due is { } instant && instant <= fireInstant)
            {
                await grain.FireDueSchedulesAsync();
            }
        }

        await Drain();
    }

    /// <summary>
    /// Drives the next round of schedule-timeout fires deterministically, without
    /// hardcoding the cap. Reads the soonest timeout instant across every grain a
    /// Command has been routed to, advances the virtual clock to it, fires the
    /// timeout on every grain now at or past its cap, and drains so the timeout
    /// outcome (the <c>OnScheduleTimeoutAsync</c> compensation, or the dead-letter
    /// when no hook is written) lands on the <see cref="Timeline"/>. A no-op when no
    /// schedule is capped. Chainable and global, like
    /// <see cref="FireDueSchedulesAsync"/>.
    /// </summary>
    public async Task FireScheduleTimeoutsAsync()
    {
        var fireables = _context.RoutedGrains.Keys
            .Select(routed => _cluster.GrainFactory.GetGrain<IEdictScheduleFireable>(routed.Key, routed.GrainClassName))
            .ToArray();

        DateTimeOffset? soonest = null;
        var timeoutByGrain = new List<(IEdictScheduleFireable Grain, DateTimeOffset? Timeout)>(fireables.Length);
        foreach (var fireable in fireables)
        {
            var timeout = await fireable.PeekSoonestScheduleTimeoutAsync();
            timeoutByGrain.Add((fireable, timeout));
            if (timeout is { } instant && (soonest is null || instant < soonest))
            {
                soonest = instant;
            }
        }

        if (soonest is null)
        {
            return;
        }

        var now = _context.Clock.GetUtcNow();
        if (soonest.Value > now)
        {
            _context.Clock.Advance(soonest.Value - now);
        }

        var fireInstant = _context.Clock.GetUtcNow();
        foreach (var (grain, timeout) in timeoutByGrain)
        {
            if (timeout is { } instant && instant <= fireInstant)
            {
                await grain.FireDueScheduleTimeoutsAsync();
            }
        }

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

    // Re-point IEdictSender at the recording decorator wrapping the real sender,
    // so a saga's in-silo dispatched Command and a test's client Command share
    // one timeline. Last AddSingleton wins in MS DI.
    static void DecorateSender(IServiceCollection services, HarnessContext ctx) =>
        services.AddSingleton<IEdictSender>(serviceProvider =>
            new RecordingSender(
                new EdictSender(
                    serviceProvider.GetRequiredService<CommandRouteResolver>(),
                    serviceProvider.GetRequiredService<IGrainFactory>(),
                    serviceProvider.GetRequiredService<EdictPrincipalStamper>(),
                    serviceProvider.GetRequiredService<EdictTenantStamper>()),
                ctx.Recorder,
                serviceProvider.GetRequiredService<CommandRouteResolver>(),
                ctx.RoutedGrains));

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
            siloBuilder.Services.AddEdictOutbox();

            // Turn auditing on with in-memory stores when the builder asked for it:
            // the resolver yields the test principal (re-read per send so ActAs takes
            // effect), WithAudit arms capture and the default repository, and the
            // shared in-memory stores stand in for a substrate so the drain has
            // somewhere to land and EdictTestApp.Audit reads the same instances back.
            if (ctx.AuditEnabled)
            {
                siloBuilder.Services.AddEdictAudit(() => ctx.CurrentPrincipal);
                siloBuilder.Services.AddSingleton<IEdictAuditStore>(ctx.AuditStore);
                siloBuilder.Services.AddSingleton<IEdictAuditPayloadStore>(ctx.PayloadStore);
                siloBuilder.WithAudit();
            }

            // Turn tenancy on when the builder asked for it: the resolver yields the
            // tenant RunAsTenant set (re-read per send and per read), and AddEdictTenant
            // arms the isolation call filter so a stolen route key into another tenant is
            // denied on the silo. A bare WithTenancy() with no RunAsTenant resolves null,
            // which fails closed at a tenant-scoped origin — the correct default.
            if (ctx.TenancyEnabled)
            {
                siloBuilder.Services.AddEdictTenant(() => ctx.CurrentTenant);
            }

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
                var inst = ActivatorUtilities.CreateInstance<InProcPublishExecutor>(serviceProvider, ctx.Recorder, ctx.RoutedGrains);
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

            DecorateSender(siloBuilder.Services, ctx);

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

            // The client is where a test's SendAsync originates, so it carries the
            // same resolver as the silo: the origin stamper reads it to attribute the
            // command before it leaves the client, the way an edge would in production.
            if (ctx.AuditEnabled)
            {
                clientBuilder.Services.AddEdictAudit(() => ctx.CurrentPrincipal);
            }

            // The client is where a test's SendAsync and ambient-scoped reads originate,
            // so it carries the same tenant resolver as the silo: the origin stamper reads
            // it to fold the tenant before the command leaves the client, and the
            // tenant-scoped readers compose it into the partition they query.
            if (ctx.TenancyEnabled)
            {
                clientBuilder.Services.AddEdictTenant(() => ctx.CurrentTenant);
            }

            DecorateSender(clientBuilder.Services, ctx);

            foreach (var apply in ctx.Replacements)
            {
                apply(clientBuilder.Services);
            }
        }
    }
}
