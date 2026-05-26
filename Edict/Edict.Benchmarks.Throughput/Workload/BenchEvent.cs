using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace Edict.Benchmarks.Throughput.Workload;

/// <summary>
/// The single event raised in the Events scenario. Carries only the
/// <c>AggregateId</c> as <c>[EdictRouteKey]</c>; the projection row written
/// by <see cref="BenchProjectionBuilder"/> is keyed by this value, and the
/// issuer's <c>IEdictTableRepository.GetAsync</c> poll matches on it.
/// </summary>
[EdictStream("Bench")]
public sealed partial record BenchEvent(Guid AggregateId) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;
}
