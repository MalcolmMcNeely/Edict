using Orleans.Runtime;

namespace Edict.Core.Outbox;

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
