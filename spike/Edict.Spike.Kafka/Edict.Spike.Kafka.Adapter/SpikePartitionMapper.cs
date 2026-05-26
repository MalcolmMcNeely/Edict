using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Spike.Kafka.Adapter;

public sealed class SpikePartitionMapper : IConsistentRingStreamQueueMapper
{
    readonly QueueId[] _queues;

    public SpikePartitionMapper(int partitionCount)
    {
        if (partitionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(partitionCount));
        }
        _queues = new QueueId[partitionCount];
        for (uint i = 0; i < partitionCount; i++)
        {
            _queues[i] = QueueId.GetQueueId("spike-kafka", i, i);
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
        foreach (var q in _queues)
        {
            if (range.InRange(q.GetUniformHashCode()))
            {
                yield return q;
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
