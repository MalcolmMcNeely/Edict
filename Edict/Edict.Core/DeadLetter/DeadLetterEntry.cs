using Edict.Core.Outbox;

namespace Edict.Core.DeadLetter;

/// <summary>
/// The terminal tail of the Outbox (ADR 0019): an <see cref="OutboxEntry"/> a
/// permanently failing effect was moved into, in the same one grain-state write
/// as its removal from the pending slice — the move is atomic by construction.
/// Records when and why so a read-only inspection seam can diagnose without a
/// writable store. Persisted state, so a frozen string-literal <c>[Alias]</c>.
/// </summary>
[GenerateSerializer]
[Alias("DeadLetterEntry")]
public sealed record DeadLetterEntry
{
    [Id(0)]
    public OutboxEntry Entry { get; init; } = new();

    [Id(1)]
    public DateTimeOffset DeadLetteredAt { get; init; }

    [Id(2)]
    public string? Reason { get; init; }
}
