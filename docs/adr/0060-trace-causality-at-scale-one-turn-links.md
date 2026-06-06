# Trace causality at scale: a trace is one grain turn, links across turn boundaries

Status: Accepted

> **Supersedes [ADR-0003](0003-parent-child-spans-across-stream-hop.md).** ADR-0003 chose parent-child across the stream hop ("one command, one trace"); this ADR reverses that hop (and every other cross-turn hop) to an `ActivityLink`, for the reasons below. The persisted W3C context ADR-0003 introduced stays on the wire unchanged in shape; only its semantic changes from a parent pointer to a link carrier.

The governing invariant for how Edict causality maps to OpenTelemetry:

> **A trace is one grain's single synchronous turn. Parent-child nests spans within that turn. An `ActivityLink` connects every asynchronous handoff to another turn or activation.**

A "turn" is the synchronous work Orleans schedules as one unit: the command-handle that validates, mutates, and raises; the event-handle that dedups and runs the consumer; the schedule or saga-timeout fire that drains its raised effects. Spans within a turn nest parent-child as normal. The moment causality crosses to *another* turn — the stream hop from publish to handle, a saga's fire-and-forget `SendCommand`, a schedule or cap reminder firing, a recovery drain, a dead-letter promotion running in a decoupled drain turn — the new turn starts its own trace and carries one `ActivityLink` back to the span that caused it.

The persisted W3C context already on the wire is the universal **link carrier**: `EdictEvent.TraceId/SpanId/TraceState`, `OutboxEntry.TraceParent/TraceState`, and the arm-context fields added to `ScheduleEntry` (`[Id(5)]`/`[Id(6)]`) and saga lifecycle state. Each stamps the context of the turn that armed or published, and the consuming turn rebuilds an `ActivityLink` from it (`ActivityExtensions.BuildLink`; a null or malformed context yields no link rather than a throw). The wire shape does not change for any existing type — only the read-side meaning of those fields moves from "my parent" to "the cause I link to."

The one cross-activation hop that stays parent-child is the API `edict.command` (Web) → `edict.command.handle` (silo): a synchronous awaited grain RPC whose caller span genuinely contains the callee, so nesting is honest. Claim-check PUT nests in its producer turn; claim-check GET nests under the consumer-process root (`edict.event.handle`). Both are parented from the available context explicitly, never from ambient `Activity.Current` (which is null at producer-enqueue and at consumer-handle entry — the original orphan-root defect).

## Why links, not parent-child, at scale

ADR-0003 chose parent-child for readability: engineers expect the handler nested under the command that caused it, and links are weakly visualised in Jaeger and Tempo. That trade is right for a single-process demo and wrong for a distributed fleet, for three concrete reasons:

- **Unbounded mega-traces.** Parent-child across every async/durable hop means one command that fans into a saga that dispatches commands that raise events that drive more sagas produces a single trace with thousands of spans spanning many silos. It is slow to query, expensive to store whole, and impossible to tail-sample coherently. The per-turn model bounds every trace to one grain turn — a small, fast, self-contained unit.
- **Broken waterfalls.** A durable hop can deliver minutes or hours after the publishing turn closed (a recovery drain, a schedule fire, a redelivery). A child span that starts after its parent ended is a malformed waterfall in every tracing UI. A link has no temporal-containment expectation, so a time-decoupled cause renders honestly.
- **Cross-silo parent spans that closed elsewhere.** Parent-child across the process boundary relies on a parent span that lives in another silo's already-flushed trace. The link makes the cross-silo causal edge an explicit, navigable, first-class fact carried on the durable context, surviving the process boundary without depending on in-process `Activity` flow.

The cost is the one ADR-0003 named: links are less prominently rendered than parent-child nesting, so an operator follows a link rather than reading a single waterfall. We accept it, because at scale the alternative is not a clean waterfall — it is a malformed, unbounded one. Tail sampling (or a link-aware sampler) is the lever that keeps a whole link-group together; head sampling at `edict.command` controls volume. See [`observability.md`](../operations/observability.md).

## Span map

Each row is one trace (one turn). Every span either nests in its turn or links to its cause; nothing is a bare orphan.

| Trace (one turn) | Root span | Links to |
|---|---|---|
| API command | `edict.command` (Web) → `edict.command.handle` (silo) → `edict.event.publish` | — (true root) |
| Event consumed | `edict.event.handle` → any `edict.command.send` | → the `edict.event.publish` that raised it |
| Dedup-suppressed redelivery | `edict.event.deduplicated` | → the originating `edict.event.publish` |
| Saga-dispatched command handled | `edict.command.handle` (linked root, not child) | → the saga's `edict.command.send` |
| Schedule fire | `edict.schedule.fire` → raised effects | → the command that armed the schedule |
| Saga-timeout fire | `edict.saga.timeout` → compensation effects | → the context that armed the saga |
| Dead-letter promotion | `edict.dead_letter.promote` | → the failing entry's originating context |
| Recovery drain | `edict.event.publish` (root) | → the staging command via the entry's persisted context |

Span names are centralised in `SemanticConventions` and asserted prefix-only by the span-emission tests and the substrate `StartsWith` guards; the full taxonomy lives in [`telemetry.md`](../usage/concepts/telemetry.md).

## Sampling contract

Each trace makes its own head decision — links, not shared trace membership, connect turns. The link carries the producing turn's *real* sampled flag: `CaptureToRequestContext` records the command span's flag, and `RestoreFromStrings` / `RestoreFromTraceParent` honour the flag byte rather than hard-coding `ActivityTraceFlags.Recorded` (the old forcing defect, which both disabled head sampling and left recorded Edict children under dropped non-Edict parents). A dropped command produces a fully-dropped turn-trace; a sampled command produces a complete one. To keep link-groups together at the collector, the operator runs tail sampling or a link-aware sampler — head sampling alone can sample one turn in and its linked cause out.

## Considered Options

- **Keep ADR-0003 parent-child across the stream hop** — rejected for distributed scale. It produces the unbounded mega-traces, broken waterfalls, and cross-silo dangling parents described above. It remains the more readable choice for a single-process demo, which is why ADR-0003 chose it; the reversal is explicitly a scale decision, not a correction of a mistake.
- **A two-class split** (bounded synchronous chains stay parent-child; only time-decoupled fires become links) — rejected. The boundary between "bounded chain" and "decoupled fire" is not knowable at span-start time (a drain may run inline in the command turn or minutes later in a recovery turn), so the model would need a staleness heuristic to switch a late drain between child and link. The uniform per-turn invariant handles recovery drains without that heuristic: every cross-turn hop is a link, full stop.
- **A staleness-detection heuristic** that picks child vs link by how late the drain runs — rejected as complexity for no benefit once the uniform invariant is adopted.
- **Amend ADR-0003 in place** rather than superseding it — rejected. The decision reverses ADR-0003's central choice; an amended ADR would read as self-contradictory. A clean supersede with a back-reference preserves the history of why parent-child was chosen first.
- **Listen to the substrate's own OTel** (Azure/Kafka/Postgres SDK spans) to stitch causality — rejected and out of scope. Edict telemetry must stand alone: the SDK spans are deliberately dropped as noise, and a future backend may emit no usable OTel at all. Causality rides Edict's own spans and links, nothing else.
