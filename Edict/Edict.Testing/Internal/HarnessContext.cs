using System.Collections.Concurrent;
using System.Reflection;

using Edict.Contracts.Audit;
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
    bool auditEnabled,
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
    public bool AuditEnabled => auditEnabled;
    public IReadOnlyList<Action<IServiceCollection>> Replacements => replacements;

    // The in-memory audit stores the silo drains to and EdictTestApp.Audit reads
    // from — one shared instance each so the read surface sees exactly what the
    // silo wrote. Only meaningful when AuditEnabled.
    public InMemoryAuditStore AuditStore { get; } = new();
    public InMemoryAuditPayloadStore PayloadStore { get; } = new();

    // The actor the audit edge resolver yields for an originating send. Mutable so
    // EdictTestApp.ActAs can re-attribute subsequent sends; the default keeps a bare
    // WithAudit() from tripping the origin fail-closed.
    public EdictPrincipal CurrentPrincipal { get; set; } = EdictTestApp.DefaultPrincipal;

    // Captured by the silo's executor-factory delegate so the test-process
    // Drain loop can reach into the silo-side held queue and flush it. Null
    // until the outbox drain engine resolves the executor for the first time
    // (i.e. before any event has been published, when there is nothing to
    // flush anyway).
    public InProcPublishExecutor? PublishExecutor { get; set; }

    /// <summary>
    /// Every <c>(grainClassName, key)</c> the recording sender has routed a
    /// Command to. A schedule is only ever started from inside a Command's
    /// <c>HandleAsync</c>, so this set is the complete roster of grains that
    /// could hold a due schedule — the discovery surface for
    /// <c>FireDueSchedulesAsync</c> with no production-side registry.
    /// </summary>
    public ConcurrentDictionary<(string GrainClassName, Guid Key), byte> RoutedGrains { get; } = new();

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
