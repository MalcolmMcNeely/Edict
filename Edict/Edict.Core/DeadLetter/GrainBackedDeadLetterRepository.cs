using Edict.Contracts.DeadLetter;
using Edict.Contracts.Projections;

namespace Edict.Core.DeadLetter;

// The dead-letter projection is a singleton List Projection Builder, so it rides
// the same read mechanism as every other projection: the reader addresses the
// grain by its singleton routing key and the grain maps to its own store
// partition internally. ListAsync filters the fleet-wide partition by source.
sealed class GrainBackedDeadLetterRepository(IEdictProjectionReader<EdictDeadLetterEntry> reader)
    : IEdictDeadLetterRepository
{
    static string SingletonPartition => EdictDeadLetterRaised.SingletonGrainKey.ToString();

    public Task<IReadOnlyList<EdictDeadLetterEntry>> ListAllAsync(
        CancellationToken cancellationToken = default) =>
        reader.QueryPartitionAsync(SingletonPartition, cancellationToken);

    public async Task<IReadOnlyList<EdictDeadLetterEntry>> ListAsync(
        string grainKey, CancellationToken cancellationToken = default)
    {
        var all = await reader.QueryPartitionAsync(
            SingletonPartition, cancellationToken).ConfigureAwait(false);
        return all.Where(entry => entry.SourceGrainKey == grainKey).ToList();
    }
}
