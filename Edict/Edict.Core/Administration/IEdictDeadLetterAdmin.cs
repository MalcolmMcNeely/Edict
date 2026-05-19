using Edict.Contracts.DeadLetter;

using Orleans;

namespace Edict.Core.Administration;

/// <summary>
/// Operator recovery surface for the dead-letter tail of the Outbox (ADR 0019).
/// Redrive is the <b>only</b> mutation path for a dead-lettered entry and it is
/// a grain method (not a writable repository) so the
/// <c>DeadLetter → Outbox</c> move is atomic in the one grain-state write;
/// inspection is the read-only <c>IEdictDeadLetterRepository</c>. Every grain
/// root (<c>EdictCommandHandler&lt;TState&gt;</c> and
/// <c>EdictIdempotencyBase&lt;TPayload&gt;</c>) implements this, so an operator
/// targets a specific aggregate by grain-class prefix. Hand-written (not
/// Edict-generated) so Orleans' own serializer codegen can see it (ADR 0006);
/// consumer-observable, hence brand-prefixed (ADR 0017). It cannot live in the
/// bare-named <c>Edict.Core.DeadLetter</c> namespace, so it sits in
/// <c>Edict.Core.Administration</c>.
/// </summary>
public interface IEdictDeadLetterAdmin : IGrainWithGuidKey
{
    /// <summary>
    /// Moves the dead-lettered entry with <paramref name="entryId"/> back to
    /// the Outbox FIFO tail with its <c>AttemptCount</c> reset, in one atomic
    /// grain-state write, then drains. A no-op if no such entry exists.
    /// </summary>
    Task RedriveAsync(Guid entryId);

    /// <summary>
    /// Read-only snapshot of this aggregate's dead-lettered effects for
    /// operator inspection. This is what the read-only
    /// <c>IEdictDeadLetterRepository</c> seam delegates to — the slice lives
    /// only in grain state (no external store, ADR 0019), so the grain is the
    /// source of truth.
    /// </summary>
    Task<IReadOnlyList<EdictDeadLetterEntry>> ListDeadLetterAsync();
}
