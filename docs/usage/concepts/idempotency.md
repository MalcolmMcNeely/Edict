# Idempotency

`EdictIdempotencyBase` is the inheritance root shared by event handlers, sagas, and projection builders. It suppresses at-least-once redeliveries via a bounded per-grain dedup window keyed by `EventId`. Consumers do not derive from it directly — `EdictEventHandler`, `EdictSaga<TProgress>`, and `EdictProjectionBuilder` ride it.

The window size comes from `EdictOptions.IdempotencyWindowSize` (silo-wide default). Override `WindowSize` on a specific consumer when a high-throughput grain type needs a larger ring than the default:

```csharp
using Edict.Core.EventHandler;

public sealed partial class HighThroughputEventHandler : EdictEventHandler
{
    protected override int WindowSize => 4096;

    public Task HandleAsync(OrderPlacedEvent edictEvent) => Task.CompletedTask;
}
```

## Surface

- **`EdictIdempotencyBase<TPayload>`** (`Edict.Core.Idempotency`) where `TPayload : IEdictPersistedState, new()`. The non-generic shim `EdictIdempotencyBase` closes the generic on `EdictUnit` for payload-free roles.
- **`WindowSize`** (`protected virtual int`) — the maximum number of distinct `EventId`s remembered in the dedup window for this grain type. The default reads `EdictOptions.IdempotencyWindowSize` once per activation and caches the value.
- **Dedup is per consuming grain, not global.** The same event delivered to an event handler and to a projection builder commits one ring slot on each — both consumers run. A redelivery to either consumer is suppressed by that consumer's own ring.
- **Dedup is keyed by `EventId`,** the framework-assigned Guid stamped at outbox drain. The route key does not deduplicate; only `EventId` does.
- A suppressed redelivery emits an `edict.event.deduplicated` span (no payload tags) so a forensic dedup hit is visible in traces.

The dedup ring slot, the consumer's payload mutation, and any staged outbox effect (a saga's `SendCommand`, a table-projection's `UpsertRow`) commit in the same one grain-state write — ring-equals-row atomicity. A throw from the consumer's `HandleAsync` leaves the ring slot uncommitted; the framework redelivers.

## Analyzer rules

`EdictIdempotencyBase` itself has no consumer-side analyzer. The persisted state contract on `TPayload` is gated by:

- **EDICT011** — `TPayload` must carry `[GenerateSerializer]`, `[Alias("literal")]`, and `[Id(n)]` on every declared public property.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Idempotency Base`, `Event`, `Event Handler`, `Saga`, `Projection Builder`.
- Concepts — [events.md](events.md), [event-handlers.md](event-handlers.md), [sagas.md](sagas.md), [projection-builders.md](projection-builders.md), [telemetry.md](telemetry.md).
