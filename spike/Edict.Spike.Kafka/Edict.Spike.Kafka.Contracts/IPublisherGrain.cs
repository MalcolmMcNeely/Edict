namespace Edict.Spike.Kafka.Contracts;

public interface IPublisherGrain : IGrainWithStringKey
{
    Task PublishAsync(Guid orderId, OrderPlaced evt);
    Task PublishManyAsync(Guid orderId, OrderPlaced[] events);
}
