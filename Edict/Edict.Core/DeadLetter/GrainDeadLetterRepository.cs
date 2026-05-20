using Edict.Contracts.DeadLetter;
using Edict.Core.Administration;

using Orleans;

namespace Edict.Core.DeadLetter;

/// <summary>
/// Grain-backed <see cref="IEdictDeadLetterRepository"/> (ADR 0019). The
/// dead-letter slice lives only in the aggregate's grain-state document — there
/// is deliberately no external store (an external store would reopen the
/// two-store atomicity gap the Outbox closes) — so the read seam delegates to
/// the grain via <see cref="IEdictDeadLetterAdmin"/>. Provider-neutral: it
/// couples to <see cref="IGrainFactory"/>, never to a storage provider's
/// private row layout. Constructed per aggregate grain class (the
/// <paramref name="grainClassPrefix"/> disambiguates, since every grain root
/// implements the admin interface). Bare-named — the consumer types only the
/// <see cref="IEdictDeadLetterRepository"/> seam.
/// </summary>
public sealed class GrainDeadLetterRepository(IGrainFactory grainFactory, string grainClassPrefix)
    : IEdictDeadLetterRepository
{
    public Task<IReadOnlyList<EdictDeadLetterEntry>> ListAsync(
        string grainKey, CancellationToken cancellationToken = default)
    {
        var admin = grainFactory.GetGrain<IEdictDeadLetterAdmin>(
            Guid.Parse(grainKey), grainClassPrefix);
        return admin.ListDeadLetterAsync();
    }

    // The pivot to ADR 0022 (forensic-only, table-projection-backed) replaces
    // this grain-backed repository with a table-backed one in a follow-up
    // slice; the fleet-wide query has no grain-level analogue, so this stub
    // exists only to keep the build green until that delete lands.
    public Task<IReadOnlyList<EdictDeadLetterEntry>> ListAllAsync(
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            "Fleet-wide dead-letter listing is provided by the table-backed " +
            "repository introduced under ADR 0022.");
}
