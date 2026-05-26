using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Spike.Kafka.Adapter;

public sealed class SpikeKafkaBatchContainer : IBatchContainer
{
    readonly object[] _events;
    readonly Dictionary<string, object>? _requestContext;

    public StreamId StreamId { get; }
    public StreamSequenceToken SequenceToken { get; }
    public int Partition { get; }
    public long Offset { get; }

    public SpikeKafkaBatchContainer(StreamId streamId, int partition, long offset, object[] events, Dictionary<string, object>? requestContext)
    {
        StreamId = streamId;
        Partition = partition;
        Offset = offset;
        SequenceToken = new EventSequenceTokenV2(offset);
        _events = events;
        _requestContext = requestContext;
    }

    public IEnumerable<Tuple<T, StreamSequenceToken>> GetEvents<T>()
    {
        for (var i = 0; i < _events.Length; i++)
        {
            yield return Tuple.Create((T)_events[i], (StreamSequenceToken)new EventSequenceTokenV2(Offset, i));
        }
    }

    public bool ImportRequestContext()
    {
        if (_requestContext == null)
        {
            return false;
        }
        RequestContextExtensions.Import(_requestContext);
        return true;
    }
}
