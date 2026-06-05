# Projection builders

An `EdictProjectionBuilder` consumes the live event stream and maintains a current-state read model, processing the stream only forward — Edict is event-driven, not event-sourced, so there is no replay or rebuild-from-history.

```csharp
using Edict.Contracts.Events;
using Edict.Core.Projections;

public sealed partial class OrderCountProjectionBuilder : EdictProjectionBuilder
{
    int _ordersPlaced;

    Task HandleAsync(OrderPlacedEvent edictEvent)
    {
        _ordersPlaced++;
        return Task.CompletedTask;
    }
}
```

## Surface

- **`EdictProjectionBuilder`** (`Edict.Core.Projections`) — abstract marker base for the projection-builder role. Inherits the dedup ring and the implicit stream subscription from `EdictIdempotencyBase`. A consumer declares a `partial class` (the generator emits the dispatch switch and the stream subscription attribute) and writes one `Task HandleAsync(TEvent edictEvent)` per subscribed event type.
- The base has no durable payload — `EdictProjectionBuilder` is the appropriate base only for projections whose state is rebuilt from zero each activation (counters, fixed-window rollups). For a durable read model use `EdictListProjectionBuilder<TRow>` instead — see [table-projections.md](table-projections.md).
- The dedup ring suppresses at-least-once redelivery per grain; see [idempotency.md](idempotency.md).

A projection builder only ever sees events from the moment it is subscribed. There is no "rebuild the projection" operation and no historical scan.

## Reading the read model

A read model that lives in an external store (`EdictListProjectionBuilder<TRow>`) is read through `IEdictProjectionReader<TRow>`, which routes through the projection grain rather than the store directly — so the read API carries no storage detail and the activation that owns the rows is on the read path. That read path supports **read-your-writes**: pass the `EdictCursor` from a Command's `Accepted` result and the read waits, briefly and boundedly, until that Command's work is visible. See [read-your-writes.md](read-your-writes.md). A base `EdictProjectionBuilder` whose state is rebuilt each activation exposes its state through its own bespoke grain interface, not the reader.

## Analyzer rules

- **EDICT001** — concrete projection builders must be declared `partial`; the generator emits the Orleans interface and the dispatch switch into a second partial declaration.
- **EDICT009** — every `HandleAsync` method must return `Task` (not `Task<T>`) and take a single parameter that derives from `EdictEvent`.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Projection Builder`, `List Projection Builder`, `Projection Reader`, `Event`, `Idempotency Base`.
- Concepts — [table-projections.md](table-projections.md), [read-your-writes.md](read-your-writes.md), [events.md](events.md), [idempotency.md](idempotency.md), [event-handlers.md](event-handlers.md), [telemetry.md](telemetry.md).
