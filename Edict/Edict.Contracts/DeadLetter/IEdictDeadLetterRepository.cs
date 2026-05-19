namespace Edict.Contracts.DeadLetter;

/// <summary>
/// Read-only inspection of an aggregate's dead-lettered Outbox effects (ADR
/// 0019). The Azure implementation lives in <c>Edict.Azure</c>; this interface
/// is the substitution seam (ADR 0008) operators and tooling bind to,
/// mirroring <see cref="TableStorage.IEdictTableRepository{T}"/> for the
/// projection side. <b>Strictly read-only</b>: redrive is a state mutation and
/// belongs on the grain (<c>IEdictDeadLetterAdmin.RedriveAsync</c>, atomic), so
/// the repository never exposes a write — exactly the Table Repository seam
/// split.
/// </summary>
public interface IEdictDeadLetterRepository
{
    /// <summary>
    /// Lists the dead-lettered effects currently held by the aggregate grain
    /// with key <paramref name="grainKey"/>. Empty when none (or the grain has
    /// no persisted state yet).
    /// </summary>
    Task<IReadOnlyList<EdictDeadLetterEntry>> ListAsync(
        string grainKey, CancellationToken cancellationToken = default);
}
