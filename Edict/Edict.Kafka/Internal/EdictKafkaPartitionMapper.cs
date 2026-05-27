using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Kafka.Internal;

/// <summary>
/// Maps Orleans <see cref="StreamId"/> values to a Kafka partition index and
/// exposes the partition set as Orleans <see cref="QueueId"/> values. The
/// hash is stable (FNV-1a 32) over the stream key string, so the same
/// aggregate routes to the same partition across silos and restarts — that's
/// the property the per-aggregate ordering guarantee rides on.
/// </summary>
sealed class EdictKafkaPartitionMapper : IConsistentRingStreamQueueMapper
{
    const string QueueNamePrefix = "edict-kafka";

    readonly QueueId[] _queues;

    public EdictKafkaPartitionMapper(int partitionCount)
    {
        if (partitionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionCount));
        }

        _queues = new QueueId[partitionCount];
        for (uint i = 0; i < partitionCount; i++)
        {
            _queues[i] = QueueId.GetQueueId(QueueNamePrefix, i, i);
        }
    }

    public IEnumerable<QueueId> GetAllQueues() => _queues;

    public QueueId GetQueueForStream(StreamId streamId)
    {
        var hash = StableHash(streamId);
        var index = (int)(hash % (uint)_queues.Length);
        return _queues[index];
    }

    public IEnumerable<QueueId> GetQueuesForRange(IRingRange range)
    {
        foreach (var queue in _queues)
        {
            if (range.InRange(queue.GetUniformHashCode()))
            {
                yield return queue;
            }
        }
    }

    public static int PartitionFor(QueueId queueId) => (int)queueId.GetNumericId();

    static uint StableHash(StreamId streamId)
    {
        var key = streamId.GetKeyAsString() ?? string.Empty;
        unchecked
        {
            uint h = 2166136261u;
            foreach (var c in key)
            {
                h = (h ^ c) * 16777619u;
            }
            return h;
        }
    }
}
