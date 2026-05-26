namespace Edict.Spike.Kafka.Contracts;

public interface ISpikeProbeGrain : IGrainWithIntegerKey
{
    Task<List<SpikeProbeEntry>> SnapshotAsync();
    Task ResetAsync();
}

[GenerateSerializer, Alias("Edict.Spike.Kafka.Contracts.SpikeProbeEntry")]
public sealed record SpikeProbeEntry
{
    [Id(0)] public DateTimeOffset Stamp { get; init; }
    [Id(1)] public long Ordinal { get; init; }
    [Id(2)] public string Kind { get; init; } = string.Empty;
    [Id(3)] public string PartitionKey { get; init; } = string.Empty;
    [Id(4)] public long? Offset { get; init; }
    [Id(5)] public int? Partition { get; init; }
    [Id(6)] public Guid? EventId { get; init; }
}
