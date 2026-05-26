using Edict.Contracts.Persistence;

namespace Edict.Benchmarks.Throughput.Workload;

/// <summary>
/// Projection row written by <see cref="BenchProjectionBuilder"/> in the
/// Events scenario. Deliberately empty — the PRD specifies "no payload
/// column" so the measured cost is dominated by framework + substrate write
/// path, not by projection application work. The row's existence (keyed by
/// pk/rk) is the completion signal the issuer polls for.
/// </summary>
[GenerateSerializer]
[Alias("Edict.Benchmarks.Throughput.Workload.BenchEventRow")]
public sealed class BenchEventRow : IEdictPersistedState
{
}
