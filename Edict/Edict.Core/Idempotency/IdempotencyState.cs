namespace Edict.Core.Idempotency;

/// <summary>
/// The bounded window of recently handled <c>EventId</c>s — the dedup-ring
/// state co-located with the consumer payload and the Outbox slice on the
/// persisted grain envelope (ADR 0018). The buffer is a circular array
/// implementation detail; the field name reflects ADR 0002's
/// commit-after-success contract rather than the data structure. Persisted
/// state, so a frozen string-literal <c>[Alias]</c> survives a class rename
/// (ADR 0017); <c>ORLEANS0010</c> is never suppressed.
/// </summary>
[Alias("IdempotencyState")]
[GenerateSerializer]
public sealed class IdempotencyState
{
    [Id(0)]
    public Guid[] HandledEventIds { get; set; } = [];

    [Id(1)]
    public int Head { get; set; }

    [Id(2)]
    public int Count { get; set; }
}
