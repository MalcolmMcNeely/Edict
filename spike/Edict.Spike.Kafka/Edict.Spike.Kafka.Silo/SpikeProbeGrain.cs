using Edict.Spike.Kafka.Adapter;
using Edict.Spike.Kafka.Contracts;

namespace Edict.Spike.Kafka.Silo;

public sealed class SpikeProbeGrain : Grain, ISpikeProbeGrain
{
    readonly SpikePreCriterionLog _log;

    public SpikeProbeGrain(SpikePreCriterionLog log)
    {
        _log = log;
    }

    public Task<List<SpikeProbeEntry>> SnapshotAsync()
    {
        var list = _log.Snapshot().Select(e => new SpikeProbeEntry
        {
            Stamp = e.Stamp,
            Ordinal = e.Ordinal,
            Kind = e.Kind.ToString(),
            PartitionKey = e.PartitionKey,
            Offset = e.Offset,
            Partition = e.Partition,
            EventId = e.EventId,
        }).ToList();
        return Task.FromResult(list);
    }

    public Task ResetAsync()
    {
        _log.Reset();
        return Task.CompletedTask;
    }
}
