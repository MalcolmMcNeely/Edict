using System.Reflection;

using Edict.Contracts.Commands;
using Edict.Contracts.Sending;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;

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
            new InMemoryEdictTableStoreFactory());

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
        var stableWindow = TimeSpan.FromMilliseconds(1500);
        var timeout = TimeSpan.FromSeconds(30);
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

    static void InvokeGeneratedAddEdict(IServiceCollection services, Assembly consumerAssembly)
    {
        var type = consumerAssembly.GetType("Edict.Generated.EdictServiceCollectionExtensions")
            ?? throw new InvalidOperationException(
                $"Consumer assembly '{consumerAssembly.GetName().Name}' has no generated " +
                "Edict.Generated.EdictServiceCollectionExtensions. Reference Edict.Generators " +
                "as an analyzer from the consumer project.");
        var method = type.GetMethod("AddEdict", BindingFlags.Public | BindingFlags.Static, [typeof(IServiceCollection)])
            ?? throw new InvalidOperationException("Generated AddEdict(IServiceCollection) not found.");
        method.Invoke(null, [services]);
    }

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

            InvokeGeneratedAddEdict(siloBuilder.Services, ctx.ConsumerAssembly);
            siloBuilder.Services.AddEdictOutbox();

            // Swap the bare PublishEvent executor for the recording decorator
            // (the single Event choke point). The drain engine indexes
            // executors by Kind, so the original must be removed, not added to.
            var original = siloBuilder.Services.Single(d =>
                d.ServiceType == typeof(IOutboxEffectExecutor)
                && d.ImplementationType == typeof(PublishEventExecutor));
            siloBuilder.Services.Remove(original);
            siloBuilder.Services.AddSingleton<IOutboxEffectExecutor>(sp =>
                new RecordingPublishEventExecutor(
                    ActivatorUtilities.CreateInstance<PublishEventExecutor>(sp),
                    sp.GetRequiredService<Serializer>(),
                    ctx.Recorder));

            DecorateSender(siloBuilder.Services, ctx.Recorder);

            siloBuilder.UseInMemoryReminderService();
            siloBuilder.AddMemoryGrainStorage("PubSubStore");
            siloBuilder.AddMemoryGrainStorage("edict-dedup");
            siloBuilder.AddMemoryGrainStorage("edict-state");
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
            InvokeGeneratedAddEdict(clientBuilder.Services, ctx.ConsumerAssembly);
            DecorateSender(clientBuilder.Services, ctx.Recorder);
        }
    }
}

