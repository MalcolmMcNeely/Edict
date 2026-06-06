# Events

An `EdictEvent` is a notification that state has changed, published to a named domain stream and addressed by the single `Guid` property carrying `[EdictRouteKey]`.

```csharp
using Edict.Contracts.Events;

[EdictStream("Orders")]
public sealed partial record OrderPlacedEvent(
    [property: EdictRouteKey] Guid OrderId) : EdictEvent;
```

A Command Handler raises an event through the protected `Raise` method on `EdictCommandHandler<TState>`. The framework stamps the framework fields and the consumer never sets them, each at its own moment: `OccurredAt` at the `Raise` call (intent time), `EventId` once as the event enters the outbox (its stable delivery identity, reused on any re-publish), and the W3C trace fields per publish at drain.

```csharp
Raise(new OrderPlacedEvent(command.OrderId));
```

## Surface

- **`EdictEvent`** (`Edict.Contracts.Events`, abstract record) — carries `EventId`, `OccurredAt`, and the W3C `TraceId` / `SpanId` / `TraceState`. All five are framework-stamped: `OccurredAt` at `Raise`, `EventId` once at outbox enqueue, the trace fields per publish at drain. Concrete events derive as `partial record` and add only their domain payload.
- **`[EdictStream(name)]`** (`Edict.Contracts.Events`, on class) — names the domain stream the concrete event belongs to. The publisher's flush target and every subscriber's implicit subscription are derived from this name.
- **`[EdictRouteKey]`** (`Edict.Contracts.Commands`, on property) — marks the one `Guid` property that addresses the event. On an event the route key selects the stream key; subscribers (handlers, sagas, projection builders) are activated with that Guid. The event's route key is independent of the command's — a saga commonly re-keys across domains.

A consumer never sees `EdictEventEnvelope` on a `HandleAsync` signature; the receiver pipeline unwraps the wire-format envelope before dispatch. See [claim-check.md](claim-check.md) for the oversized-event escape hatch.

## Analyzer rules

- **EDICT003** — concrete events must have exactly one `[EdictRouteKey]` property of type `Guid`.
- **EDICT007** — concrete events must be declared `partial`; the generator emits the Orleans `[Alias]` into a second partial declaration.
- **EDICT008** — concrete events must declare `[EdictStream(name)]`; omitting it causes silent stream misrouting and so is an error at compile time.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Event`, `RouteKey`, `Domain Stream`, `Event Envelope`.
- Concepts — [commands.md](commands.md), [event-handlers.md](event-handlers.md), [projections.md](projections.md), [sagas.md](sagas.md), [claim-check.md](claim-check.md), [telemetry.md](telemetry.md).
