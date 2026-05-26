namespace Edict.Spike.Kafka.Contracts;

[GenerateSerializer, Alias("Edict.Spike.Kafka.Contracts.OrderPlaced")]
public sealed record OrderPlaced
{
    [Id(0)] public Guid OrderId { get; init; }
    [Id(1)] public Guid EventId { get; init; }
    [Id(2)] public int Sequence { get; init; }
    [Id(3)] public string Note { get; init; } = string.Empty;
}
