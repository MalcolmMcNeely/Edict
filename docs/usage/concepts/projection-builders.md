# Projection builders

An `EdictProjectionBuilder` consumes the live event stream and maintains a current-state read model, processing the stream only forward — Edict is event-driven, not event-sourced, so there is no replay or rebuild-from-history.

```csharp
using Edict.Contracts.Events;
using Edict.Core.Projections;

public sealed partial class OrderCountProjectionBuilder : EdictProjectionBuilder
{
    int _ordersPlaced;

    public Task HandleAsync(OrderPlacedEvent edictEvent)
    {
        _ordersPlaced++;
        return Task.CompletedTask;
    }
}
```

## Surface

- **`EdictProjectionBuilder`** (`Edict.Core.Projections`) — abstract marker base for the projection-builder role. Inherits the dedup ring and the implicit stream subscription from `EdictIdempotencyBase`. A consumer declares a `partial class` (the generator emits the dispatch switch and the stream subscription attribute) and writes one `Task HandleAsync(TEvent edictEvent)` per subscribed event type.
- The base has no durable payload — `EdictProjectionBuilder` is the appropriate base only for projections whose state is rebuilt from zero each activation (counters, fixed-window rollups). For a durable read model use `EdictTableProjectionBuilder<T>` instead — see [table-projections.md](table-projections.md).
- The dedup ring suppresses at-least-once redelivery per grain; see [idempotency.md](idempotency.md).

A projection builder only ever sees events from the moment it is subscribed. There is no "rebuild the projection" operation and no historical scan.

## Analyzer rules

- **EDICT001** — concrete projection builders must be declared `partial`; the generator emits the Orleans interface and the dispatch switch into a second partial declaration.
- **EDICT009** — every `HandleAsync` method must return `Task` (not `Task<T>`) and take a single parameter that derives from `EdictEvent`.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Projection Builder`, `Table Projection Builder`, `Event`, `Idempotency Base`.
- Concepts — [table-projections.md](table-projections.md), [events.md](events.md), [idempotency.md](idempotency.md), [event-handlers.md](event-handlers.md), [telemetry.md](telemetry.md).
