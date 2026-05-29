using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Kafka.Internal;

/// <summary>
/// Maps Orleans <see cref="StreamId"/> values to a (topic, partition) Kafka
/// coordinate and exposes the full coordinate set as Orleans
/// <see cref="QueueId"/> values. One Kafka topic per <c>[EdictStream]</c>
/// domain, with the route key's stable hash selecting a
/// partition inside that topic's fan-out so per-aggregate ordering is
/// preserved. The topic is encoded into the <see cref="QueueId"/>'s string
/// prefix and decoded by <see cref="TopicFor"/> so the adapter's receiver
/// factory knows which Kafka topic to subscribe to without an out-of-band
/// queue → topic map.
/// </summary>
sealed class EdictKafkaPartitionMapper : IConsistentRingStreamQueueMapper
{
    const string QueueNamePrefixRoot = "edict-kafka-";

    readonly QueueId[] _allQueues;
    readonly Dictionary<string, QueueId[]> _queuesByStream;

    public EdictKafkaPartitionMapper(EdictKafkaStreamsOptions options, EdictKafkaStreamRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(registry);

        _queuesByStream = new Dictionary<string, QueueId[]>(StringComparer.Ordinal);
        var all = new List<QueueId>();

        foreach (var streamName in registry.StreamNames)
        {
            var partitions = options.PartitionCountFor(streamName);
            if (partitions <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    $"EdictKafkaStreamsOptions.PartitionCountFor(\"{streamName}\") returned {partitions}; partition count must be positive.");
            }

            var prefix = QueueNamePrefixRoot + streamName;
            var queues = new QueueId[partitions];
            for (uint p = 0; p < partitions; p++)
            {
                queues[p] = QueueId.GetQueueId(prefix, p, StableSpread(streamName, p));
            }

            _queuesByStream[streamName] = queues;
            all.AddRange(queues);
        }

        _allQueues = all.ToArray();
    }

    public IEnumerable<QueueId> GetAllQueues() => _allQueues;

    public QueueId GetQueueForStream(StreamId streamId)
    {
        var ns = streamId.GetNamespace() ?? string.Empty;
        if (!_queuesByStream.TryGetValue(ns, out var queues))
        {
            throw new InvalidOperationException(
                $"Edict.Kafka has no registered topic for stream '{ns}'. Every concrete event must carry [EdictStream(\"...\")] and be reachable from an assembly loaded by the silo's AppDomain when AddEdictKafkaStreams runs.");
        }

        var hash = StableHash(streamId.GetKeyAsString() ?? string.Empty);
        var index = (int)(hash % (uint)queues.Length);
        return queues[index];
    }

    public IEnumerable<QueueId> GetQueuesForRange(IRingRange range)
    {
        foreach (var queue in _allQueues)
        {
            if (range.InRange(queue.GetUniformHashCode()))
            {
                yield return queue;
            }
        }
    }

    public static int PartitionFor(QueueId queueId) => (int)queueId.GetNumericId();

    public static string TopicFor(QueueId queueId)
    {
        var prefix = queueId.GetStringNamePrefix();
        if (!prefix.StartsWith(QueueNamePrefixRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"QueueId prefix '{prefix}' is not an Edict.Kafka queue (expected prefix '{QueueNamePrefixRoot}').");
        }
        return prefix.Substring(QueueNamePrefixRoot.Length);
    }

    static uint StableHash(string key)
    {
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

    static uint StableSpread(string topic, uint partition)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (var c in topic)
            {
                h = (h ^ c) * 16777619u;
            }
            h = (h ^ ':') * 16777619u;
            h = (h ^ partition) * 16777619u;
            return h;
        }
    }
}
