using Edict.Spike.Kafka.Contracts;

namespace Edict.Spike.Kafka.Silo;

public sealed class RecorderGrain : Grain, IRecorderGrain
{
    readonly List<OrderPlaced> _seen = new();

    public Task RecordAsync(OrderPlaced evt)
    {
        _seen.Add(evt);
        return Task.CompletedTask;
    }

    public Task<List<OrderPlaced>> GetAllAsync() => Task.FromResult(_seen.ToList());

    public Task ResetAsync()
    {
        _seen.Clear();
        return Task.CompletedTask;
    }
}
