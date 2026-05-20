using Edict.Contracts.Events;
using Edict.Core.Outbox;

namespace Edict.Core.DeadLetter;

/// <summary>
/// Engine-facing seam for promoting a failing <see cref="OutboxEntry"/> to a
/// dead-letter publish entry (ADR 0022). The pure mapping lives in
/// <see cref="DeadLetterPromotion"/>; this seam adds the Orleans-serializer
/// and route-resolver dependencies needed at runtime, so the composed
/// <see cref="OutboxHost{TPayload}"/> stays a plain class that passes its
/// own grain-key / grain-type strings in. Bare-named — no consumer types it.
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

    /// <summary>
    /// Receiver-side promotion for an inbound event whose claim-check blob
    /// could not be materialised after <c>MaxAttempts</c> retries (ADR 0024).
    /// Extends the dead-letter conceptual surface — previously "outbound effect
    /// failed at the producer" — to also include "inbound event could not be
    /// materialised at the consumer." Returns a fresh
    /// <see cref="OutboxEffectKind.PublishEvent"/> entry the caller stages on
    /// its own Outbox so the resulting <c>EdictDeadLetterRaised</c> rides the
    /// standard at-least-once dead-letter stream and lands in the fleet-wide
    /// forensic projection alongside the existing publisher-side failures.
    /// </summary>
    OutboxEntry PromoteBlobMissing(
        EdictEventEnvelope envelope,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset now);
}
