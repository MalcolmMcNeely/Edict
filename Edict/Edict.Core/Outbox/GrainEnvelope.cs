using System.ComponentModel;

using Edict.Core.Idempotency;

namespace Edict.Core.Outbox;

/// <summary>
/// The single persisted grain-state document: the consumer payload
/// (aggregate state for a Command Handler, the progress type for a saga,
/// <c>EdictUnit</c> for stateless event handlers and projection builders)
/// plus the Outbox slice and the dedup-ring state as sibling slots — so a
/// state change, its outbound effect, and the dedup-ring commit all land
/// in one atomic write. Command Handlers simply never touch
/// <see cref="Idempotency"/>; the cost is one empty
/// <see cref="IdempotencyState"/>. No consumer names this — it is bare, like
/// <see cref="IdempotencyState"/>. Persisted state, so a frozen string-literal
/// <c>[Alias]</c> survives a class rename even when the shape changes (slots
/// have been dropped under this alias in past evolutions);
/// <c>ORLEANS0010</c> is never suppressed.
/// <para>
/// Public because the consumer-facing <c>EdictCommandHandler&lt;TState&gt;</c>
/// and <c>EdictIdempotencyBase&lt;TPayload&gt;</c> bases inherit
/// <c>Grain&lt;GrainEnvelope&lt;TState&gt;&gt;</c> — flipping to internal fires
/// CS9338 inside <c>Edict.Core</c>. Hidden from consumer IntelliSense because
/// the consumer's own code never types this; it is reached purely through the
/// base-class chain.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
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
