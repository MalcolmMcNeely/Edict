using MessagePack;

namespace Edict.Contracts.DeadLetter;

/// <summary>
/// Read-only projection of a dead-lettered Outbox effect for operator
/// inspection (ADR 0019). The internal persisted entry lives in the bare-named
/// <c>Edict.Core.DeadLetter</c> engine; this is the consumer-facing shape the
/// read-only <see cref="IEdictDeadLetterRepository"/> returns, so it is
/// brand-prefixed and lives in the Orleans-free shared kernel. MessagePack
/// annotations like every other contract type (ADR 0005/0007).
/// </summary>
[MessagePackObject(keyAsPropertyName: true)]
public sealed record EdictDeadLetterEntry
{
    /// <summary>Stable id of the dead-lettered effect; the redrive key.</summary>
    public Guid EntryId { get; init; }

    /// <summary>The effect kind name (e.g. <c>PublishEvent</c>), as a string so the kernel stays Core-free.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>How many attempts were made before the effect was dead-lettered.</summary>
    public int AttemptCount { get; init; }

    /// <summary>When the effect crossed from the Outbox into the DeadLetter slice.</summary>
    public DateTimeOffset DeadLetteredAt { get; init; }

    /// <summary>The failure reason captured at dead-letter time, if any.</summary>
    public string? Reason { get; init; }
}
