using System.Collections.Concurrent;
using System.Reflection;

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
