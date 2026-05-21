namespace Edict.Core.Outbox;

/// <summary>
/// A durable pending side-effect co-located in the one grain-state write.
/// Immutable: the slice's pure transitions produce new entries, never mutate in place.
/// Persisted grain state, so a frozen string-literal <c>[Alias]</c> survives a class
/// rename; <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[GenerateSerializer]
[Alias("OutboxEntry")]
public sealed record OutboxEntry
{
    [Id(0)]
    public Guid EntryId { get; init; }

    [Id(1)]
    public OutboxEffectKind Kind { get; init; }

    /// <summary>The effect payload, serialized by the engine (opaque to the slice).</summary>
    [Id(2)]
    public byte[] Payload { get; init; } = [];

    /// <summary>W3C <c>traceparent</c> captured at enqueue so a crash-recovery drain still nests under the originating span.</summary>
    [Id(3)]
    public string? TraceParent { get; init; }

    [Id(4)]
    public string? TraceState { get; init; }

    [Id(5)]
    public int AttemptCount { get; init; }

    [Id(6)]
    public DateTimeOffset NextAttemptUtc { get; init; }
}
