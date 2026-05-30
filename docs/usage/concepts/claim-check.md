# Claim check

Claim-check is the framework's escape hatch for oversized events. When the serialised wire frame would exceed the streaming substrate's per-message cap, the payload is written to an append-only blob store and the wire hop carries a small pointer string instead. The receiver pipeline materialises the body before dispatch; the consumer's `HandleAsync(TEvent)` signature is unchanged.

```csharp
public Task HandleAsync(LargeOrderEvent edictEvent)
{
    return Task.CompletedTask;
}
```

## Engagement

- The publish-time decision happens at the outbox commit boundary, not inside `Raise`. The serialised event is measured; if its size exceeds the configured threshold, the body is written to the claim-check store and the queued wire frame is an `EdictEventEnvelope` carrying a `ClaimCheckKey` pointer.
- The threshold is a streaming-substrate option — every substrate's own caps differ (Azure Queue Storage messages are limited to ~48 KB; Azure Table single-string properties to 32 KB). The default leaves ~2 KB framing headroom under the relevant cap. See the wiring page for your streaming substrate for the exact knob and default.
- If the wrapped envelope still exceeds the storage per-property cap after wrapping, the framework throws `EdictEnvelopeOverflowException` (`Edict.Contracts.ClaimCheck`) at the commit boundary, surfacing a designed framework failure rather than a deep-substrate error.

## Lifecycle

- The store is **append-only** by design. The `IEdictClaimCheckStore` surface has only `PutAsync` and `GetAsync` — no `DeleteAsync` and no `ExistsAsync`. The framework never deletes a blob.
- Retention is the operator's responsibility, set via the storage account's lifecycle policy. A framework bug or a configuration mistake cannot erase forensic evidence.
- A missing blob at receive time (the storage lifecycle has already reaped it) surfaces as a fetch exception which the receiver pipeline funnels into the dead-letter promotion path with `EdictDeadLetterFailureKind.BlobMissing`. See [dead-letter.md](dead-letter.md).

## Storage backends per substrate

- **Azure Blob** — `AzureBlobClaimCheckStore` in `Edict.Azure.Streaming`. Wired via the dedicated `AddEdictAzureBlobClaimCheck` extension so it can be paired with any streaming choice (Azure Queue Storage, Kafka, etc.).
- **Postgres** — `PostgresClaimCheckStore` in `Edict.Postgres`. Wired alongside the Postgres persistence/streaming setup.
- The store is a separate dependency from the streaming substrate — a consumer running Kafka streaming can still use Azure Blob claim-check. See the wiring pages for substrate-specific defaults.

## Surface

- **`IEdictClaimCheckStore`** (`Edict.Contracts.ClaimCheck`) — `PutAsync(payload, cancellationToken)` and `GetAsync(key, cancellationToken)` only. Framework-internal seam; consumers do not call it.
- **`EdictEventEnvelope`** (`Edict.Contracts.Events`) — the universal wire-format wrapper. Consumers never derive from it and never see it on a `HandleAsync` signature.
- **`EdictEnvelopeOverflowException`** (`Edict.Contracts.ClaimCheck`) — thrown at the commit boundary when the wrapped bytes still exceed the storage per-property cap. Carries `RouteKey`, `EventType`, and `MeasuredBytes`.

## Analyzer rules

None — claim-check engagement is a runtime decision driven by event size against the configured threshold.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Claim Check`, `Event Envelope`, `Event`, `Outbox`.
- Concepts — [events.md](events.md), [dead-letter.md](dead-letter.md), [telemetry.md](telemetry.md).
- ADR — [0020 — Claim check for oversized events](../../adr/0020-claim-check.md).
