using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Kafka.Internal;

/// <summary>
/// Orleans <see cref="IBatchContainer"/> wrapping one Kafka record. The
/// <see cref="Offset"/> drives manual commit timing in
/// <see cref="EdictKafkaReceiver.MessagesDeliveredAsync"/> — the broker offset
/// is advanced only after the handler returns, so a mid-handler crash leaves
/// the offset un-committed and Kafka redelivers on restart (ADR-0002
/// at-least-once + dedup).
/// </summary>
sealed class EdictKafkaBatchContainer : IBatchContainer
{
    readonly object[] _events;

    public EdictKafkaBatchContainer(StreamId streamId, int partition, long offset, object[] events)
    {
        StreamId = streamId;
        Partition = partition;
        Offset = offset;
        SequenceToken = new EventSequenceTokenV2(offset);
        _events = events;
    }

    public StreamId StreamId { get; }

    public StreamSequenceToken SequenceToken { get; }

    public int Partition { get; }

    public long Offset { get; }

    public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>()
    {
        for (var i = 0; i < _events.Length; i++)
        {
            yield return Tuple.Create((T)_events[i], (StreamSequenceToken)new EventSequenceTokenV2(Offset, i));
        }
    }

    public bool ImportRequestContext() => false;
}
