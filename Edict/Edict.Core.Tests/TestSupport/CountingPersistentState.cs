using Orleans.Runtime;

namespace Edict.Core.Tests.TestSupport;

sealed class CountingPersistentState<T>(CallLog log, string sourceTag = "state") : IPersistentState<T>
    where T : new()
{
    public T State { get; set; } = new();

    public string Etag => string.Empty;
    public bool RecordExists => true;

    public Task WriteStateAsync()
    {
        log.Record(sourceTag, nameof(WriteStateAsync));
        return Task.CompletedTask;
    }

    public Task ReadStateAsync() => Task.CompletedTask;
    public Task ClearStateAsync() => Task.CompletedTask;
}
