namespace Edict.Spike.Kafka.Contracts;

public interface IRecorderGrain : IGrainWithStringKey
{
    Task RecordAsync(OrderPlaced evt);
    Task<List<OrderPlaced>> GetAllAsync();
    Task ResetAsync();
}
