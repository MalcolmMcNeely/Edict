using Edict.Contracts.Commands;

namespace Edict.Benchmarks.Throughput.Workload;

/// <summary>
/// Events-scenario workload: handler raises one <see cref="BenchEvent"/>.
/// Exercises the full pipeline — Outbox + stream provider + consumer dispatch
/// + dedup ring + projection write. The ~256-byte <c>Filler</c> matches
/// <see cref="BenchIncrementCommand"/> so the two scenarios share payload
/// shape and only differ on what the handler does.
/// <para>
/// <c>CorrelationId</c> is a per-send token minted by the benchmark issuer.
/// It travels into the raised event and becomes the projection row key so
/// each send produces a distinct row, making the issuer's wait an honest
/// full-pipeline signal rather than a stale-row short-circuit.
/// </para>
/// </summary>
public sealed partial record BenchPublishCommand(Guid AggregateId, Guid CorrelationId, byte[] Filler) : EdictCommand
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;

    public Guid CorrelationId { get; init; } = CorrelationId;

    public byte[] Filler { get; init; } = Filler;
}
