using Edict.Core.Outbox;

namespace Edict.Core.DeadLetter;

/// <summary>
/// Engine-facing seam for promoting a failing <see cref="OutboxEntry"/> to a
/// dead-letter publish entry (ADR 0022). The pure mapping lives in
/// <see cref="DeadLetterPromotion"/>; this seam adds the Orleans-serializer
/// and route-resolver dependencies needed at runtime, so the engine itself
/// stays a plain class that takes its identity inputs from
/// <see cref="IOutboxHost"/>. Bare-named — no consumer types it.
/// </summary>
interface IDeadLetterPromoter
{
    /// <summary>
    /// Builds a new <see cref="OutboxEffectKind.PublishEvent"/> entry carrying
    /// an <c>EdictDeadLetterRaised</c> notification for <paramref name="failed"/>.
    /// Pure with respect to grain state — the engine swaps the failed head for
    /// the returned entry inside its single commit.
    /// </summary>
    OutboxEntry Promote(
        OutboxEntry failed,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset now);
}
