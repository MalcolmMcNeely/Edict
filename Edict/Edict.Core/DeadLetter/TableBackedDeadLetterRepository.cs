using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;

namespace Edict.Core.DeadLetter;

sealed class TableBackedDeadLetterRepository(IEdictTableRepository<EdictDeadLetterEntry> table)
    : IEdictDeadLetterRepository
{
    public Task<IReadOnlyList<EdictDeadLetterEntry>> ListAllAsync(
        CancellationToken cancellationToken = default) =>
        table.QueryPartitionAsync(EdictDeadLetterTable.Name, cancellationToken);

    public async Task<IReadOnlyList<EdictDeadLetterEntry>> ListAsync(
        string grainKey, CancellationToken cancellationToken = default)
    {
        var all = await table.QueryPartitionAsync(
            EdictDeadLetterTable.Name, cancellationToken).ConfigureAwait(false);
        return all.Where(e => e.SourceGrainKey == grainKey).ToList();
    }
}
