using Edict.Contracts.Routing;

namespace Edict.Benchmarks.Throughput.Workload;

/// <summary>
/// Folds a bench aggregate id into the external-store partition key the framework
/// writes its List-projection rows under. A per-aggregate List projection
/// partitions on the projection grain's primary key, which the runtime composes
/// from the route Guid through <see cref="EdictKeyComposer"/> — N-format, no tenant
/// for these public bench aggregates. The harness's store-direct reads must fold
/// the id identically or every point-get addresses a partition the grain never
/// wrote to, so the row is never found.
/// </summary>
static class BenchProjectionPartitionKey
{
    public static string For(Guid aggregateId) => EdictKeyComposer.Compose(null, aggregateId.ToString("N"));
}
