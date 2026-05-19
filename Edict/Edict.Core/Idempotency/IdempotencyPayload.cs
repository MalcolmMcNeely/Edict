namespace Edict.Core.Idempotency;

/// <summary>
/// The payload an <c>EdictIdempotencyBase&lt;TPayload&gt;</c> stores inside the
/// grain-state envelope: the dedup <see cref="Ring"/> alongside the consumer
/// payload (<c>EdictUnit</c> for event handlers/projection builders, the
/// progress type for a saga). Bare-named persisted state, so a frozen
/// string-literal <c>[Alias]</c> (ADR 0017); <c>ORLEANS0010</c> never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("IdempotencyPayload`1")]
public sealed class IdempotencyPayload<TPayload>
    where TPayload : new()
{
    [Id(0)]
    public IdempotencyState Ring { get; set; } = new();

    [Id(1)]
    public TPayload Payload { get; set; } = new();
}
