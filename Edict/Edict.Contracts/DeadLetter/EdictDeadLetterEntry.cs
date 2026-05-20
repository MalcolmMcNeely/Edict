using Edict.Contracts.Persistence;

using MessagePack;

namespace Edict.Contracts.DeadLetter;

/// <summary>
/// Read-only projection row for a dead-lettered Outbox effect (ADR 0022). The
/// dead-letter mechanism is forensic-only — when an Outbox entry exhausts
/// <c>MaxAttempts</c> the engine emits an <see cref="EdictDeadLetterRaised"/>
/// event, the built-in projection upserts this row, and operators inspect it
/// via <see cref="IEdictDeadLetterRepository"/>. Brand-prefixed (consumer-typed
/// per CONTEXT.md clause a) and lives in the Orleans-free shared kernel.
/// MessagePack annotations like every other contract type (ADR 0005/0007).
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record EdictDeadLetterEntry : IEdictPersistedState
{
    /// <summary>Stable id of the dead-lettered effect; the projection row key.</summary>
    public Guid EntryId { get; init; }

    /// <summary>The effect kind name (e.g. <c>PublishEvent</c>) as a string so the kernel stays Core-free.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Final attempt count at the moment the entry was dead-lettered.</summary>
    public int AttemptCount { get; init; }

    /// <summary>When the engine promoted the failing entry to a dead-letter publish.</summary>
    public DateTimeOffset DeadLetteredAt { get; init; }

    /// <summary>Grain key of the source aggregate whose Outbox produced the failed effect.</summary>
    public string SourceGrainKey { get; init; } = string.Empty;

    /// <summary>Full type name of the source aggregate's grain class.</summary>
    public string SourceGrainType { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable identifier of the failed effect's target — encoded per kind:
    /// <c>"{stream}/{eventType}"</c> for <c>PublishEvent</c>,
    /// <c>"{targetGrainType}/{targetGrainKey}"</c> for <c>SendCommand</c>,
    /// <c>"{table}/{pk}/{rk}"</c> for <c>UpsertRow</c>.
    /// </summary>
    public string EffectTarget { get; init; } = string.Empty;

    /// <summary>The W3C <c>traceparent</c> captured on the original Outbox entry; null when no active trace (ADR 0003).</summary>
    public string? TraceParent { get; init; }

    /// <summary>Full type name of the captured exception, so operators can filter without parsing strings.</summary>
    public string? ExceptionType { get; init; }

    /// <summary>The captured exception <see cref="Exception.Message"/> — not the stack trace.</summary>
    public string? Reason { get; init; }

    /// <summary>The failed effect's payload as JSON for operator inspection; display data, distinct from the MessagePack wire format (ADR 0007).</summary>
    public string? PayloadJson { get; init; }

    /// <summary>
    /// Full type name of the source <c>EdictEvent</c> that triggered the failing
    /// effect — populated only for <c>InvokeHandler</c> promotions (ADR 0023),
    /// null for <c>PublishEvent</c> / <c>SendCommand</c> / <c>UpsertRow</c>.
    /// Lets operators filter dead letters by event type without parsing payload bytes.
    /// </summary>
    public string? SourceEventType { get; init; }

    /// <summary>
    /// <c>EventId</c> of the source event that triggered the failing effect —
    /// populated only for <c>InvokeHandler</c> promotions (ADR 0023), null
    /// otherwise. Pairs with <see cref="SourceEventType"/> to uniquely identify
    /// the originating event for RCA.
    /// </summary>
    public Guid? SourceEventId { get; init; }

    /// <summary>
    /// Claim-check store key for the inbound event whose blob could not
    /// be fetched at the receiver (ADR 0024). Populated only when
    /// <see cref="FailureKind"/> is
    /// <see cref="EdictDeadLetterFailureKind.BlobMissing"/>; null on the
    /// existing publisher-side <see cref="EdictDeadLetterFailureKind.EffectFailure"/>
    /// path. Lets operators click through to the (likely lifecycle-reaped)
    /// blob to diagnose the misconfiguration.
    /// </summary>
    public string? ClaimCheckKey { get; init; }

    /// <summary>
    /// Discriminator over the two dead-letter failure modes (ADR 0024).
    /// Defaults to <see cref="EdictDeadLetterFailureKind.EffectFailure"/>
    /// so every existing publisher-side promotion remains valid without
    /// touching the call site.
    /// </summary>
    public EdictDeadLetterFailureKind FailureKind { get; init; } = EdictDeadLetterFailureKind.EffectFailure;
}
