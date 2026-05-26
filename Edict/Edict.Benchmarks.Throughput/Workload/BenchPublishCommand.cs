using Edict.Contracts.Commands;

namespace Edict.Benchmarks.Throughput.Workload;

/// <summary>
/// Events-scenario workload: handler raises one <see cref="BenchEvent"/>.
/// Exercises the full pipeline — Outbox + stream provider + consumer dispatch
/// + dedup ring + projection write. The ~256-byte <c>Filler</c> matches
/// <see cref="BenchIncrementCommand"/> so the two scenarios share payload
/// shape and only differ on what the handler does.
/// </summary>
public sealed partial record BenchPublishCommand(Guid AggregateId, byte[] Filler) : EdictCommand
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;

    public byte[] Filler { get; init; } = Filler;
}
