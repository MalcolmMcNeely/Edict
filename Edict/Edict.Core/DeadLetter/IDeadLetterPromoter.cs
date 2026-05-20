using Edict.Core.Outbox;

namespace Edict.Core.DeadLetter;

/// <summary>
/// Engine-facing seam for promoting a failing <see cref="OutboxEntry"/> to a
/// dead-letter publish entry (ADR 0022 / 0026). The pure mapping lives in
/// <see cref="DeadLetterPromotion"/>; this seam adds the Orleans-serializer
/// and route-resolver dependencies needed at runtime, so the composed
/// <see cref="OutboxHost{TPayload}"/> stays a plain class that passes its
/// own grain-key / grain-type strings in. After the ADR-0026 fold the
/// receiver-side missing-blob promotion path is no longer a separate entry
/// point — when the engine promotes an <see cref="OutboxEffectKind.InvokeHandler"/>
/// entry whose payload is a pointer-bearing envelope, <see cref="Promote"/>
/// detects the shape and routes it through the BlobMissing failure-kind
/// mapping. Bare-named — no consumer types it.
/// </summary>
interface IDeadLetterPromoter
{
    /// <summary>
    /// Builds a new <see cref="OutboxEffectKind.PublishEvent"/> entry carrying
    /// an <c>EdictDeadLetterRaised</c> notification for <paramref name="failed"/>.
    /// Pure with respect to grain state — the engine swaps the failed entry
    /// for the returned entry inside its single commit.
    /// </summary>
    OutboxEntry Promote(
        OutboxEntry failed,
        Exception exception,
        string sourceGrainKey,
        string sourceGrainType,
        DateTimeOffset now);
}
