using Edict.Core.Projections;

namespace Edict.Benchmarks.Throughput.Workload;

/// <summary>
/// In-grain per-aggregate counter for the State-species saturation pass. Runs the
/// identical workload as <see cref="BenchCounterListProjectionBuilder"/> —
/// increment a per-aggregate counter on every <see cref="BenchEvent"/> — but keeps
/// the count in the grain's own durable payload, committed inline with the dedup
/// ring in a single write. The EPS delta against the List counter isolates the
/// storage-commit cost: one inline state write versus the external
/// <c>UpsertRow</c> drain.
/// </summary>
public sealed partial class BenchCounterStateProjectionBuilder : EdictProjectionBuilder<BenchCounterProjection>
{
    Task HandleAsync(BenchEvent edictEvent)
    {
        Projection.Count++;
        return Task.CompletedTask;
    }
}
