using Edict.Contracts.Events;
using Edict.Core.EventHandler;

using Orleans;

namespace Edict.Tests.Conformance.Outbox;

public interface ICounterAppliedProbe : IGrainWithGuidKey
{
    Task<int> GetAppliedCountAsync();
    Task<IReadOnlyList<Guid>> GetAppliedEventIdsAsync();
}

// Idempotent consumer of the CounterAggregate's event. The dedup ring is keyed
// on EventId, so a producer re-publish that carries the same stable id collapses
// to a single application. The Postgres drain-recovery resilience test reads
// this probe to prove effectively-once across the ack-write-window fault: before
// EventId became stable, the re-publish minted a fresh id and the handler ran
// twice; now it runs once.
public sealed partial class CounterAppliedEventHandler : EdictEventHandler, ICounterAppliedProbe
{
    readonly List<Guid> _applied = [];

    public Task HandleAsync(CounterIncrementedEvent edictEvent)
    {
        _applied.Add(edictEvent.EventId);
        return Task.CompletedTask;
    }

    public Task<int> GetAppliedCountAsync() => Task.FromResult(_applied.Count);

    public Task<IReadOnlyList<Guid>> GetAppliedEventIdsAsync() =>
        Task.FromResult<IReadOnlyList<Guid>>(_applied.AsReadOnly());
}
