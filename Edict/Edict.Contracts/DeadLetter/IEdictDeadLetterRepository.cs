namespace Edict.Contracts.DeadLetter;

/// <summary>
/// Read-only inspection of dead-lettered Outbox effects (ADR 0022). The
/// repository reads from the fleet-wide dead-letter projection table; the
/// Azure implementation lives in <c>Edict.Azure</c> and the in-memory
/// implementation in <c>Edict.Testing</c>. This interface is the substitution
/// seam (ADR 0008) operators and tooling bind to, mirroring
/// <see cref="TableStorage.IEdictTableRepository{T}"/> for the projection
/// side. <b>Strictly read-only</b>: recovery is manual re-emission (for
/// <c>PublishEvent</c>/<c>SendCommand</c>) or manual table repair (for
/// <c>UpsertRow</c>), so the repository never exposes a write.
/// </summary>
public interface IEdictDeadLetterRepository
{
    /// <summary>
    /// Lists dead-lettered effects whose <see cref="EdictDeadLetterEntry.SourceGrainKey"/>
    /// matches <paramref name="grainKey"/>. Empty when none.
    /// </summary>
    Task<IReadOnlyList<EdictDeadLetterEntry>> ListAsync(
        string grainKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every dead-letter row in the fleet-wide partition — the
    /// operator's first triage question during a system-wide failure.
    /// </summary>
    Task<IReadOnlyList<EdictDeadLetterEntry>> ListAllAsync(
        CancellationToken cancellationToken = default);
}
