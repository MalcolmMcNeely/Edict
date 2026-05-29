using System.Collections.Concurrent;
using System.Reflection;

using Edict.Core.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Edict.Testing.Internal;

/// <summary>
/// Per-<see cref="EdictTestApp"/> state the Orleans TestingHost configurators
/// need but cannot be handed directly (Orleans instantiates configurator types
/// itself). An <see cref="AsyncLocal{T}"/> id flows from <c>StartAsync</c>
/// through cluster build into the configurators, which resolve their context
/// here — so parallel test classes each get an isolated recorder/clock.
/// </summary>
sealed class HarnessContext(
    Assembly consumerAssembly,
    TimelineRecorder recorder,
    FakeTimeProvider clock,
    InMemoryTableStoreFactory tableStoreFactory,
    SubscriberMap subscriberMap,
    ChaosOptions chaos,
    InMemoryClaimCheckStore claimCheckStore,
    int claimCheckThresholdBytes,
    IReadOnlyList<Action<IServiceCollection>> replacements)
{
    public Assembly ConsumerAssembly => consumerAssembly;
    public TimelineRecorder Recorder => recorder;
    public FakeTimeProvider Clock => clock;
    public InMemoryTableStoreFactory TableStoreFactory => tableStoreFactory;
    public SubscriberMap SubscriberMap => subscriberMap;
    public ChaosOptions Chaos => chaos;
    public InMemoryClaimCheckStore ClaimCheckStore => claimCheckStore;
    public int ClaimCheckThresholdBytes => claimCheckThresholdBytes;
    public IReadOnlyList<Action<IServiceCollection>> Replacements => replacements;

    // Captured by the silo's executor-factory delegate so the test-process
    // Drain loop can reach into the silo-side held queue and flush it. Null
    // until the outbox drain engine resolves the executor for the first time
    // (i.e. before any event has been published, when there is nothing to
    // flush anyway).
    public InProcPublishExecutor? PublishExecutor { get; set; }

    /// <summary>The silo's IEdictMetricsCache singleton, captured by the silo
    /// configurator so EdictTestApp's GetOutboxState / GetSagaState probes
    /// read the SAME cache that the OutboxHost + EdictSaga pushed to. Null
    /// until the silo's DI container instantiates the cache (i.e. before any
    /// grain has activated, when there is nothing to probe anyway).</summary>
    public EdictMetricsCache? MetricsCache { get; set; }
}

static class HarnessRegistry
{
    static readonly ConcurrentDictionary<string, HarnessContext> Contexts = new();
    static readonly AsyncLocal<string?> CurrentId = new();

    public static IDisposable Activate(string id, HarnessContext context)
    {
        Contexts[id] = context;
        CurrentId.Value = id;
        return new Scope(id);
    }

    public static HarnessContext Current =>
        CurrentId.Value is { } id && Contexts.TryGetValue(id, out var ctx)
            ? ctx
            : throw new InvalidOperationException(
                "No active EdictTestApp context on this async flow. EdictTestApp wiring " +
                "must run within StartAsync.");

    sealed class Scope(string id) : IDisposable
    {
        public void Dispose()
        {
            Contexts.TryRemove(id, out _);
            CurrentId.Value = null;
        }
    }
}
