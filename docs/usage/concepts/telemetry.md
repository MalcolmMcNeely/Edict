# Telemetry

Edict emits OpenTelemetry traces and metrics through one `ActivitySource` and one `Meter`, both named `"Edict"`. `[EdictTelemeterized]` marks primitive command and event properties for automatic tag emission as `edict.{snake_case_property_name}` on the active span.

```csharp
using Edict.Contracts.Commands;
using Edict.Contracts.Telemetry;

public sealed partial record PlaceOrderCommand(Guid OrderId, string CustomerReference) : EdictCommand
{
    [EdictRouteKey]
    [EdictTelemeterized]
    public Guid OrderId { get; init; } = OrderId;

    [EdictTelemeterized]
    public string CustomerReference { get; init; } = CustomerReference;
}
```

Register both surfaces with OpenTelemetry by source name:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("Edict"))
    .WithMetrics(metrics => metrics.AddMeter("Edict"));
```

## Trace causality: one turn, links across turns

A trace is **one grain's single synchronous turn**. Spans nest parent-child *within* that turn; the moment causality crosses to another turn — the stream hop from publish to handle, a saga's fire-and-forget command dispatch, a schedule or saga-timeout fire, a recovery drain, a dead-letter promotion — the new turn starts its own trace and carries one `ActivityLink` back to the span that caused it. Every span is therefore either nested in its turn's trace or linked to its cause; nothing is a bare orphan.

The link carrier is the W3C trace context already persisted on the wire — `EdictEvent.TraceId/SpanId/TraceState`, `OutboxEntry.TraceParent`, and the arm-context on `ScheduleEntry` and saga state. The consuming turn rebuilds an `ActivityLink` from it; the wire shape is unchanged, only the read-side meaning moved from "my parent" to "the cause I link to." The one cross-activation hop that stays parent-child is the awaited API call `edict.command` (Web) → `edict.command.handle` (silo), because the caller span genuinely contains the callee.

| Trace (one turn) | Root span | Links to |
|---|---|---|
| API command | `edict.command` (Web) → `edict.command.handle` (silo) → `edict.event.publish` | — (true root) |
| Event consumed | `edict.event.handle` → any `edict.command.send` | → the `edict.event.publish` that raised it |
| Dedup-suppressed redelivery | `edict.event.deduplicated` | → the originating `edict.event.publish` |
| Saga-dispatched command handled | `edict.command.handle` (linked root) | → the saga's `edict.command.send` |
| Schedule fire | `edict.schedule.fire` → raised effects | → the command that armed the schedule |
| Saga-timeout fire | `edict.saga.timeout` → compensation effects | → the context that armed the saga |
| Dead-letter promotion | `edict.dead_letter.promote` | → the failing entry's originating context |
| Recovery drain | `edict.event.publish` (root) | → the staging command via the entry's persisted context |

Each trace makes its own head-sampling decision, and the link carries the producing turn's real sampled flag. Tail sampling (or a link-aware sampler) is the lever that keeps a whole link-group together; head sampling at `edict.command` controls volume. The rationale for this model — bounded per-turn traces, honest waterfalls, explicit cross-silo edges — is [ADR-0060](../../adr/0060-trace-causality-at-scale-one-turn-links.md) (supersedes ADR-0003); the operator-side guidance is in [`observability.md`](../../operations/observability.md).

## Canonical `edict.*` tag taxonomy

Tag keys are stable across declaring types — the same domain property name (`OrderId`, `CustomerReference`) lands under the same key regardless of which command or event declared it. The snake-case derivation matches `System.Text.Json.JsonNamingPolicy.SnakeCaseLower` (`SKU` → `sku`, `HTTPMethod` → `http_method`, `CustomerID` → `customer_id`).

Span names:

- `edict.command.send` — issued when a saga's outbox `SendCommand` effect dispatches a command.
- `edict.command` — `IEdictSender.SendAsync` call-site span. `[EdictTelemeterized]` tags from the command land here.
- `edict.command.handle` — silo-side command handler invocation. A child of `edict.command` on the awaited API path; on a saga's fire-and-forget dispatch it is a new trace root linking back to `edict.command.send`.
- `edict.event.publish` — outbox publish of a raised event. On an inline drain (the publish runs in the same turn that raised the event) it is nested in its producer turn. On a recovery drain (reminder / activation, where the entry is rehydrated from durable state in a later, possibly cross-silo turn) it is its own trace root linking back to the staging command, per the per-turn invariant.
- `edict.event.handle` — consumer handler invocation. A new trace root linking back to the `edict.event.publish` that raised the event (the stream hop is the canonical cross-turn link). `[EdictTelemeterized]` tags from the event land on both `publish` and `handle` spans.
- `edict.event.deduplicated` — emitted (with no payload tags) when the dedup ring suppresses an at-least-once redelivery. A new trace root linking back to the originating `edict.event.publish`, so a suppressed redelivery is visible rather than silent.
- `edict.event.claim_check.put` / `edict.event.claim_check.get` — claim-check blob operations. PUT nests in the producer turn; GET nests under `edict.event.handle`.
- `edict.schedule.fire` — a schedule tick or one-shot fire. A new trace root linking back to the command that armed the schedule.
- `edict.saga.timeout` — a fired saga lifetime cap. A new trace root linking back to the context that armed the saga.
- `edict.dead_letter.promote` — a dead-letter promotion. A new trace root linking back to the failing entry's originating context, so an operator can pivot from a dead-letter row straight to the trace that produced it.
- `edict.table.upsert` — table-projection row write.
- `edict.audit.drain` — the audit-drain turn writing captured records to the WORM store off the command's hot path. A new trace root (one-shot timer, activation drain, or reminder retry), not a child of the capturing command. This span is the *sampled overlay*; the durable record lives in the audit store, distinct from any trace (see [audit-log.md](audit-log.md)).

Framework tag keys that the runtime stamps regardless of `[EdictTelemeterized]`:

- `edict.grain.type` — cross-cutting; on every grain-scoped span and metric.
- `edict.command.type`, `edict.command.route_key` — on command spans.
- `edict.event.type`, `edict.event.size_bytes`, `edict.event.claim_checked` — on event spans.
- `edict.claim_check.key`, `edict.claim_check.payload.size` — on claim-check spans.
- `edict.outbox.effect_kind` — on outbox drain spans and metrics.
- `edict.dead_letter.failure_reason` — on dead-letter metrics. A closed allowlist: `Timeout`, `Saturated`, `Serialization`, `Substrate`, `Wiring`, `ConsumerBug`, `InternalBug`, `SagaTimeout`, `SagaTerminal`, `Unhandled`.
- `edict.saga.timeout.outcome` — on the saga timeout-fired counter. A closed allowlist: `compensated`, `deadlettered`.
- `edict.idempotency.dedup.reason` — on the duplicate-suppression counter. A closed allowlist: `window` (the EventId was already in the committed dedup window) and `in_flight` (the EventId's slot was still reserved on the grain, retained as defense-in-depth since serial stream delivery makes a concurrent same-id redelivery structurally impossible).
- `edict.audit.kind`, `edict.audit.outcome` — on the audit-records-captured counter. Closed allowlists: `command`/`event` and (on commands) `accepted`/`rejected`. Never the principal, correlation id, or grain key, which are unbounded.

The full set lives in `Edict.Telemetry.SemanticConventions`.

## Metrics

Instrument names follow OpenTelemetry semantic-convention suffixes (`.count`, `.duration`, `.size`, `.age`, `.lag`). Selected examples:

- `edict.command.handle.duration` — command handler latency.
- `edict.event.handle.duration` / `edict.event.handle.lag` — consumer handler latency and stream-to-handle end-to-end delay.
- `edict.outbox.pending.count` / `edict.outbox.oldest_entry.age` — outbox depth and stuck-aggregate detection. Observable gauges; pushed from each grain into a silo-local cache.
- `edict.saga.progress.age` — time since the saga last advanced; the leading indicator of a saga approaching its absolute cap.
- `edict.saga.timeout.fired` — count of fired absolute lifetime caps, tagged `edict.saga.timeout.outcome` (`compensated` when the `OnSagaTimeoutAsync` override dispatched a Command, else `deadlettered`).
- `edict.saga.completed` — count of sagas that reached `Completed` via `Complete()`. With `timeout.fired` it makes the health ratio `fired / (fired + completed)` computable, separating a handful of timeouts among millions from a rising failure trend. Both counters are tagged by saga type (`edict.grain.type`), so they join `progress.age` on one dimension.
- `edict.dead_letter.promotion.count` / `edict.dead_letter.promotion.failure.count` — dead-letter rate and promotion failures.
- `edict.idempotency.duplicate.count` — dedup-ring hit rate, tagged `edict.idempotency.dedup.reason` (`window` or `in_flight`). Every suppressing consumer path (projection, saga, event handler, pointer-envelope intake) records it through one shared guard.
- `edict.claim_check.payload.size` — claim-check body size histogram.
- `edict.audit.records.captured` — count of audit records captured, tagged `edict.audit.kind` and `edict.audit.outcome`; the "we recorded this decision" compliance signal. `edict.audit.drain.failure` — count of audit-drain batches that could not be durably written (a record exists but is not yet durable); a compliance signal, not an effect retry, so alert on any non-zero rate. The audit store is the durable legal record these count over; the metrics and the trace are the ephemeral overlay (see [audit-log.md](audit-log.md) and [observability.md](../../operations/observability.md#audit-metrics-and-the-drain-span)).

Cardinality is bounded at compile time: no metric carries `aggregate_key` or `grain_key`. Per-grain forensic detail belongs on spans (the trace already carries `edict.command.route_key`), not on metrics. Tests that need per-aggregate specificity use a `MeterListener` (for the metric) plus an `ActivityListener` (for the span) — the dual-listener pattern.

## Analyzer rules

- **EDICT005** — `[EdictTelemeterized]` properties must be a primitive type: `bool`, `byte`, `sbyte`, `char`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`, `string`, or `Guid`. Higher-cardinality or structured types are rejected.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Telemeterized`, `EdictCommand`, `Event`.
- Concepts — [commands.md](commands.md), [events.md](events.md), [dead-letter.md](dead-letter.md), [idempotency.md](idempotency.md), [claim-check.md](claim-check.md), [audit-log.md](audit-log.md).
- ADRs — [0060 — Trace causality at scale: one turn, links across turns](../../adr/0060-trace-causality-at-scale-one-turn-links.md) (supersedes 0003), [0037 — Telemeterized tag keys carry no message-type prefix](../../adr/0037-telemeterized-tag-keys-no-type-prefix.md), [0038 — Meters naming and cross-cutting attributes](../../adr/0038-meters-naming-and-cross-cutting-attributes.md), [0039 — Metrics cardinality policy](../../adr/0039-metrics-cardinality-policy.md), [0040 — Silo-local metrics cache for observable gauges](../../adr/0040-silo-local-metrics-cache-for-observable-gauges.md).
