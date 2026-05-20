namespace Edict.Core.ClaimCheck;

/// <summary>
/// Per-grain delivery-retry tracker for receiver-side missing-blob fetches
/// (ADR 0024, slice 3). Keyed by the inbound envelope's claim-check key so a
/// burst of redeliveries for the same lifecycle-reaped blob shares one
/// attempt count and one backoff schedule — multiple distinct events
/// pointing at the same blob do not multiply the retries against the same
/// underlying I/O failure. The tracker lives as a sibling slot on
/// <see cref="Outbox.GrainEnvelope{TPayload}"/> so it commits in the same one
/// grain-state write as the dedup ring and the Outbox slice. Persisted
/// state, so a frozen string-literal <c>[Alias]</c> survives a class rename
/// (ADR 0017).
/// </summary>
[Alias("BlobMissingTracker")]
[GenerateSerializer]
public sealed class BlobMissingTracker
{
    [Id(0)]
    public Dictionary<string, BlobMissingAttempt> Attempts { get; set; } = [];
}

/// <summary>
/// Per-key retry record: how many delivery attempts have failed against this
/// blob and the earliest UTC at which the next attempt may proceed (the
/// exponential-backoff gate, computed via the same <c>OutboxBackoff</c>
/// math the publisher path uses).
/// </summary>
[Alias("BlobMissingAttempt")]
[GenerateSerializer]
public sealed record BlobMissingAttempt(
    [property: Id(0)] int AttemptCount,
    [property: Id(1)] DateTimeOffset NextAttemptUtc);
