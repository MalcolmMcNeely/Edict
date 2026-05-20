using Edict.Contracts.Commands;
using Edict.Contracts.Events;

using MessagePack;

namespace Edict.Contracts.DeadLetter;

/// <summary>
/// Forensic notification that an Outbox effect exhausted <c>MaxAttempts</c>
/// and was promoted to a dead-letter publish (ADR 0022). The engine emits one
/// of these in the same one grain-state write that removes the failed entry
/// from the Outbox, so the move is atomic by construction; the built-in
/// singleton <c>EdictDeadLetterProjectionBuilder</c> consumes the stream and
/// upserts an <see cref="EdictDeadLetterEntry"/> row. Singleton-routed via the
/// fixed <see cref="SingletonGrainKey"/> so all fleet-wide dead letters land
/// on one projection grain (the deliberate v1 trade-off for cheap fleet-wide
/// reads). Carries the same RCA payload as <see cref="EdictDeadLetterEntry"/>.
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
[EdictStream("edict-dead-letter")]
public sealed record EdictDeadLetterRaised : EdictEvent
{
    /// <summary>
    /// Fixed grain key the dead-letter projection grain is addressed by — every
    /// <see cref="EdictDeadLetterRaised"/> carries this same value as its
    /// <see cref="SingletonKey"/>, routing all fleet-wide dead letters to one
    /// singleton projection grain (ADR 0022).
    /// </summary>
    public static readonly Guid SingletonGrainKey =
        new("d1d1d1d1-d1d1-d1d1-d1d1-d1d1d1d1d1d1");

    /// <summary>
    /// The route key — fixed to <see cref="SingletonGrainKey"/> on every
    /// instance so the projection grain is a true singleton.
    /// </summary>
    [EdictRouteKey]
    public Guid SingletonKey { get; init; } = SingletonGrainKey;

    /// <summary>Stable id of the dead-lettered effect; preserved from the originating Outbox entry.</summary>
    public Guid EntryId { get; init; }

    /// <summary>The effect kind name (e.g. <c>PublishEvent</c>).</summary>
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
    /// Per-effect-kind identifier of the failed effect's target — encoded as
    /// <c>"{stream}/{eventType}"</c>, <c>"{targetGrainType}/{targetGrainKey}"</c>, or
    /// <c>"{table}/{pk}/{rk}"</c>.
    /// </summary>
    public string EffectTarget { get; init; } = string.Empty;

    /// <summary>The W3C <c>traceparent</c> captured on the originating Outbox entry (ADR 0003); null when no active trace.</summary>
    public string? TraceParent { get; init; }

    /// <summary>Full type name of the captured exception.</summary>
    public string? ExceptionType { get; init; }

    /// <summary>The captured exception <see cref="Exception.Message"/> — not the stack trace.</summary>
    public string? Reason { get; init; }

    /// <summary>The failed effect's payload as JSON for operator inspection (display data, distinct from MessagePack wire format per ADR 0007).</summary>
    public string? PayloadJson { get; init; }

    /// <summary>
    /// Full type name of the source <see cref="EdictEvent"/> that triggered the
    /// failing effect — populated only for <c>InvokeHandler</c> promotions
    /// (ADR 0023), null for <c>PublishEvent</c> / <c>SendCommand</c> / <c>UpsertRow</c>.
    /// Lets operators filter dead letters by event type without parsing payload bytes.
    /// </summary>
    public string? SourceEventType { get; init; }

    /// <summary>
    /// <see cref="EdictEvent.EventId"/> of the source event that triggered the
    /// failing effect — populated only for <c>InvokeHandler</c> promotions
    /// (ADR 0023), null otherwise. Pairs with <see cref="SourceEventType"/> to
    /// uniquely identify the originating event for RCA.
    /// </summary>
    public Guid? SourceEventId { get; init; }
}
