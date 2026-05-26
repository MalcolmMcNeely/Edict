using System.Collections.Concurrent;

namespace Edict.Spike.Kafka.Adapter;

public enum SpikeProbeKind
{
    QueueMessageBatchEnter,
    GetQueueMessagesEnter,
    GetQueueMessagesExit,
    HandleAsyncEnter,
    HandleAsyncExit,
    MessagesDeliveredAsync
}

public sealed record SpikeProbeEvent(
    DateTimeOffset Stamp,
    long Ordinal,
    SpikeProbeKind Kind,
    string PartitionKey,
    long? Offset,
    int? Partition,
    Guid? EventId);

public sealed class SpikePreCriterionLog
{
    long _ordinal;
    readonly ConcurrentQueue<SpikeProbeEvent> _events = new();

    public void Record(SpikeProbeKind kind, string partitionKey, long? offset = null, int? partition = null, Guid? eventId = null)
    {
        var ord = Interlocked.Increment(ref _ordinal);
        _events.Enqueue(new SpikeProbeEvent(DateTimeOffset.UtcNow, ord, kind, partitionKey, offset, partition, eventId));
    }

    public IReadOnlyList<SpikeProbeEvent> Snapshot() => _events.ToArray();

    public void Reset()
    {
        Interlocked.Exchange(ref _ordinal, 0);
        while (_events.TryDequeue(out _)) { }
    }
}
