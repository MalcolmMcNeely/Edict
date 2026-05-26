using Edict.Contracts.Commands;

namespace Edict.Benchmarks.Throughput.Workload;

/// <summary>
/// Commands-scenario workload: handler increments aggregate state and returns
/// <c>Accepted</c> with no <c>Raise</c>. Isolates the aggregate write path +
/// state provider — no Outbox publish, no projection, no stream.
/// </summary>
public sealed partial record BenchIncrementCommand(Guid AggregateId) : EdictCommand
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;
}
