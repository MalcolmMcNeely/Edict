using Edict.Contracts.Commands;

namespace Edict.Benchmarks.Throughput.Workload;

/// <summary>
/// Commands-scenario workload: handler increments aggregate state and returns
/// <c>Accepted</c> with no <c>Raise</c>. Isolates the aggregate write path +
/// state provider — no Outbox publish, no projection, no stream. The
/// ~256-byte <c>Filler</c> matches <see cref="BenchPublishCommand"/> so the
/// two scenarios share payload shape.
/// </summary>
public sealed partial record BenchIncrementCommand(Guid AggregateId, byte[] Filler) : EdictCommand
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;

    public byte[] Filler { get; init; } = Filler;
}
