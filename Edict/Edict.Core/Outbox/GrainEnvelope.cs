using Edict.Core.Idempotency;

namespace Edict.Core.Outbox;

/// <summary>
/// The single persisted grain-state document (ADR 0018 / 0022 / 0026): the
/// consumer payload (aggregate state for a Command Handler, the progress type
/// for a saga, <c>EdictUnit</c> for stateless event handlers and projection
/// builders) plus the Outbox slice and the dedup-ring state as sibling slots
/// — so a state change, its outbound effect, and the dedup-ring commit all
/// land in one atomic write. Command Handlers simply never touch
/// <see cref="Idempotency"/>; the cost is one empty
/// <see cref="IdempotencyState"/>. No consumer names this — it is bare, like
/// <see cref="IdempotencyState"/>. Persisted state, so a frozen string-literal
/// <c>[Alias]</c> survives a class rename even when the shape changes
/// (ADR 0017 — the pre-ADR-0022 DeadLetter slot was dropped under this alias;
/// ADR 0026 dropped the receiver-side BlobMissing tracker slot the same way);
/// <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("GrainEnvelope`1")]
public sealed class GrainEnvelope<TPayload>
    where TPayload : new()
{
    [Id(0)]
    public TPayload Payload { get; set; } = new();

    [Id(1)]
    public OutboxSlice Outbox { get; set; } = new();

    [Id(2)]
    public IdempotencyState Idempotency { get; set; } = new();
}
