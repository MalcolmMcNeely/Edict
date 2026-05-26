using Edict.Spike.Kafka.Contracts;

using Orleans.Runtime;
using Orleans.Streams;

namespace Edict.Spike.Kafka.Silo;

public sealed class PublisherGrain : Grain, IPublisherGrain
{
    public Task PublishAsync(Guid orderId, OrderPlaced evt)
    {
        var streamProvider = this.GetStreamProvider(SpikeStreamNames.StreamProvider);
        var streamId = StreamId.Create(SpikeStreamNames.OrdersNamespace, orderId);
        var stream = streamProvider.GetStream<OrderPlaced>(streamId);
        return stream.OnNextAsync(evt);
    }

    public async Task PublishManyAsync(Guid orderId, OrderPlaced[] events)
    {
        var streamProvider = this.GetStreamProvider(SpikeStreamNames.StreamProvider);
        var streamId = StreamId.Create(SpikeStreamNames.OrdersNamespace, orderId);
        var stream = streamProvider.GetStream<OrderPlaced>(streamId);
        foreach (var evt in events)
        {
            await stream.OnNextAsync(evt);
        }
    }
}
