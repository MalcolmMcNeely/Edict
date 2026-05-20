using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;

namespace Edict.Core.DeadLetter;

/// <summary>
/// Provider-agnostic facade over an <see cref="IEdictTableRepository{T}"/> of
/// <see cref="EdictDeadLetterEntry"/>, implementing the consumer-facing
/// <see cref="IEdictDeadLetterRepository"/> seam (ADR 0022). The dead-letter
/// projection writes every entry under the singleton partition
/// <see cref="DeadLetterPartition"/>; both reads are partition-scoped scans of
/// that partition (filtered by <see cref="EdictDeadLetterEntry.SourceGrainKey"/>
/// for <see cref="ListAsync"/>). Provider-specific concerns (Azure vs in-memory)
/// stay inside the injected repository — the facade is the same in every host.
/// </summary>
sealed class TableBackedDeadLetterRepository(IEdictTableRepository<EdictDeadLetterEntry> table)
    : IEdictDeadLetterRepository
{
    public Task<IReadOnlyList<EdictDeadLetterEntry>> ListAllAsync(
        CancellationToken cancellationToken = default) =>
        table.QueryPartitionAsync(EdictDeadLetterProjectionBuilder.DeadLetterPartition, cancellationToken);

    public async Task<IReadOnlyList<EdictDeadLetterEntry>> ListAsync(
        string grainKey, CancellationToken cancellationToken = default)
    {
        var all = await table.QueryPartitionAsync(
            EdictDeadLetterProjectionBuilder.DeadLetterPartition, cancellationToken);
        return all.Where(e => e.SourceGrainKey == grainKey).ToList();
    }
}
