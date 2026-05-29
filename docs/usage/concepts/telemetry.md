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

## Canonical `edict.*` tag taxonomy

Tag keys are stable across declaring types — the same domain property name (`OrderId`, `CustomerReference`) lands under the same key regardless of which command or event declared it. The snake-case derivation matches `System.Text.Json.JsonNamingPolicy.SnakeCaseLower` (`SKU` → `sku`, `HTTPMethod` → `http_method`, `CustomerID` → `customer_id`).

Span names:

- `edict.command.send` — issued at the `IEdictSender.Send` call site.
- `edict.command` — handler dispatch span. `[EdictTelemeterized]` tags from the command land here.
- `edict.event.publish` — outbox publish of a raised event.
- `edict.event.handle` — consumer handler invocation. `[EdictTelemeterized]` tags from the event land on both `publish` and `handle` spans.
- `edict.event.deduplicated` — emitted (with no payload tags) when the dedup ring suppresses an at-least-once redelivery.
- `edict.event.claim_check.get` / `edict.event.claim_check.put` — claim-check blob operations.
- `edict.table.upsert` — table-projection row write.

Framework tag keys that the runtime stamps regardless of `[EdictTelemeterized]`:

- `edict.grain.type` — cross-cutting; on every grain-scoped span and metric.
- `edict.command.type`, `edict.command.route_key` — on command spans.
- `edict.event.type`, `edict.event.size_bytes`, `edict.event.claim_checked` — on event spans.
- `edict.claim_check.key`, `edict.claim_check.payload.size` — on claim-check spans.
- `edict.outbox.effect_kind` — on outbox drain spans and metrics.
- `edict.dead_letter.failure_reason` — on dead-letter metrics. A closed allowlist: `Timeout`, `Saturated`, `Serialization`, `Substrate`, `Wiring`, `ConsumerBug`, `InternalBug`, `Unhandled`.

The full set lives in `Edict.Telemetry.SemanticConventions`.

## Metrics

Instrument names follow OpenTelemetry semantic-convention suffixes (`.count`, `.duration`, `.size`, `.age`, `.lag`). Selected examples:

- `edict.command.handle.duration` — command handler latency.
- `edict.event.handle.duration` / `edict.event.handle.lag` — consumer handler latency and stream-to-handle end-to-end delay.
- `edict.outbox.pending.count` / `edict.outbox.oldest_entry.age` — outbox depth and stuck-aggregate detection. Observable gauges; pushed from each grain into a silo-local cache.
- `edict.saga.progress.age` — time since the saga last advanced.
- `edict.dead_letter.promotion.count` / `edict.dead_letter.promotion.failure.count` — dead-letter rate and promotion failures.
- `edict.idempotency.duplicate.count` — dedup-ring hit rate.
- `edict.claim_check.payload.size` — claim-check body size histogram.

Cardinality is bounded at compile time: no metric carries `aggregate_key` or `grain_key`. Per-grain forensic detail belongs on spans (the trace already carries `edict.command.route_key`), not on metrics. Tests that need per-aggregate specificity use a `MeterListener` (for the metric) plus an `ActivityListener` (for the span) — the dual-listener pattern.

## Analyzer rules

- **EDICT005** — `[EdictTelemeterized]` properties must be a primitive type: `bool`, `byte`, `sbyte`, `char`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double`, `decimal`, `string`, or `Guid`. Higher-cardinality or structured types are rejected.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Telemeterized`, `EdictCommand`, `Event`.
- Concepts — [commands.md](commands.md), [events.md](events.md), [dead-letter.md](dead-letter.md), [idempotency.md](idempotency.md), [claim-check.md](claim-check.md).
- ADRs — [0037 — Telemeterized tag keys carry no message-type prefix](../../adr/0037-telemeterized-tag-keys-no-type-prefix.md), [0038 — Meters naming and cross-cutting attributes](../../adr/0038-meters-naming-and-cross-cutting-attributes.md), [0039 — Metrics cardinality policy](../../adr/0039-metrics-cardinality-policy.md), [0040 — Silo-local metrics cache for observable gauges](../../adr/0040-silo-local-metrics-cache-for-observable-gauges.md).
