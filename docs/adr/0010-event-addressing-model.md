# Event addressing model

Events are introduced with three deliberate choices for consistency with the command spine and for fan-out across a system with hundreds of handlers. **Same wire mechanism as commands** — a concrete event is a `record` deriving from `EdictEvent`, hand-written `[MessagePackObject(keyAsPropertyName: true)]` per ADR 0006, with only the smart `[Alias]` partial auto-emitted per ADR 0009; one mechanism, one drift-guard. **Domain streams, not per-event-type streams** — a stream is domain-scoped (e.g. `Sales`, `Orders`) named by a required `[EdictStream("Name")]` on the concrete event (no namespace default — analyzer errors if absent), and a subscriber wakes for every event type on the stream, no-op'ing those it does not handle without consuming a dedup-window slot (the generator emits a synchronous `HandlesType` gate alongside `DispatchAsync`). **The event's `[EdictRouteKey]` re-keys freely** — usually equal to the command key (in-domain) but **may differ** (cross-domain, e.g. a Sales handler raising an Orders-stream event); the continuous `command → event → handler` correlation is provided by **trace context** (ADR 0003), not by a guaranteed-equal Guid.

## Considered Options

- **Per-event-type streams** — rejected: every consumer wakes for every event type and filters in-process, doesn't scale across domains.
- **`[GenerateSerializer]`/`[Immutable]` on events** — rejected: the `EdictEvent` base in `Edict.Contracts` cannot carry generated Orleans attributes (ADR 0006 generator-ordering trap).
- **Namespace-leaf default for `[EdictStream]`** — rejected: a missing attribute silently splitting events across unintended streams is worse than a compile error.
