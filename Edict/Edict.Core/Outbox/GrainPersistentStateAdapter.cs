using Orleans.Runtime;

namespace Edict.Core.Outbox;

/// <summary>
/// Adapts the existing <c>Grain&lt;TState&gt;.State</c> +
/// <c>WriteStateAsync</c> surface into an <see cref="IPersistentState{T}"/>
/// so the bare <see cref="OutboxHost{TPayload}"/> stays a plain class — its
/// only persistence seam is the facet interface, swappable in tests for a
/// pure in-memory fake. The grain shell constructs an instance closed over
/// its own <c>base.State</c> getter/setter and <c>WriteStateAsync</c>; the
/// <c>[StorageProvider("edict-state")]</c> attribute on the shell still
/// drives the underlying <c>Grain&lt;TState&gt;</c> persistence binding,
/// so no consumer authoring change is needed. Bare-named.
/// </summary>
sealed class GrainPersistentStateAdapter<TState>(
    Func<TState> get,
    Action<TState> set,
    Func<Task> writeState) : IPersistentState<TState>
{
    public TState State
    {
        get => get();
        set => set(value);
    }

    public string Etag => string.Empty;
    public bool RecordExists => true;

    public Task WriteStateAsync() => writeState();
    public Task ReadStateAsync() => Task.CompletedTask;
    public Task ClearStateAsync() => Task.CompletedTask;
}
