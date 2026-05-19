using System.Collections.Concurrent;
using System.Reflection;

using Microsoft.Extensions.Time.Testing;

namespace Edict.Testing;

/// <summary>
/// Per-<see cref="EdictTestApp"/> state the Orleans TestingHost configurators
/// need but cannot be handed directly (Orleans instantiates configurator types
/// itself). An <see cref="AsyncLocal{T}"/> id flows from <c>StartAsync</c>
/// through cluster build into the configurators, which resolve their context
/// here — so parallel test classes each get an isolated recorder/clock.
/// </summary>
sealed class EdictTestHarnessContext(
    Assembly consumerAssembly,
    EdictTimelineRecorder recorder,
    FakeTimeProvider clock,
    InMemoryEdictTableStoreFactory tableStoreFactory)
{
    public Assembly ConsumerAssembly => consumerAssembly;
    public EdictTimelineRecorder Recorder => recorder;
    public FakeTimeProvider Clock => clock;
    public InMemoryEdictTableStoreFactory TableStoreFactory => tableStoreFactory;
}

static class EdictTestHarnessRegistry
{
    static readonly ConcurrentDictionary<string, EdictTestHarnessContext> Contexts = new();
    static readonly AsyncLocal<string?> CurrentId = new();

    public static IDisposable Activate(string id, EdictTestHarnessContext context)
    {
        Contexts[id] = context;
        CurrentId.Value = id;
        return new Scope(id);
    }

    public static EdictTestHarnessContext Current =>
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
