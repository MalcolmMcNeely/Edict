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

    public async Task<IReadOnlyList<EdictDeadLetterEntry>> ListAllAsync(
        CancellationToken cancellationToken = default)
    {
        // Forensic reads are a plain poll — no cursor — so the read status is
        // ignored and the rows are returned directly.
        var read = await reader.QueryPartitionAsync(
            SingletonPartition, cancellationToken: cancellationToken).ConfigureAwait(false);
        return read.Rows;
    }

    public async Task<IReadOnlyList<EdictDeadLetterEntry>> ListAsync(
        string grainKey, CancellationToken cancellationToken = default)
    {
        var read = await reader.QueryPartitionAsync(
            SingletonPartition, cancellationToken: cancellationToken).ConfigureAwait(false);
        return read.Rows.Where(entry => entry.SourceGrainKey == grainKey).ToList();
    }
}
