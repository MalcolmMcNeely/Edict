namespace Edict.Core.Outbox;

/// <summary>
/// The single persisted grain-state document (ADR 0018): the consumer payload
/// (aggregate state for a command handler, <c>{ Ring, payload }</c> for an
/// idempotent consumer) co-located with the Outbox/DeadLetter slice so a state
/// change and its outbound effect commit in one atomic write. No consumer
/// names this — it is bare, like <c>IdempotencyState</c>. Persisted state, so
/// a frozen string-literal <c>[Alias]</c> survives a class rename (ADR 0017);
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
}
