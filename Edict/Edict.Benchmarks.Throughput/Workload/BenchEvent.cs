using Edict.Contracts.Commands;
using Edict.Contracts.Events;

namespace Edict.Benchmarks.Throughput.Workload;

/// <summary>
/// The single event raised in the Events scenario. <c>AggregateId</c> is the
/// <c>[EdictRouteKey]</c> and becomes the projection partition key.
/// <c>CorrelationId</c> is copied from <see cref="BenchPublishCommand"/> by
/// <see cref="BenchAggregateHandler"/> and becomes the projection row key,
/// so the issuer's <c>IEdictTableRepository.GetAsync</c> poll waits for the
/// row written by this specific send.
/// </summary>
[EdictStream("Bench")]
public sealed partial record BenchEvent(Guid AggregateId, Guid CorrelationId) : EdictEvent
{
    [EdictRouteKey]
    public Guid AggregateId { get; init; } = AggregateId;

    public Guid CorrelationId { get; init; } = CorrelationId;
}
