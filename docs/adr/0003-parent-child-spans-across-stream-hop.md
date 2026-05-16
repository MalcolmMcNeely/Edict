# Parent-child spans across the async stream hop

Edict makes the command/event chain fully observable via a single `ActivitySource` ("Edict") and a `Command → Publish {Event} → Handle {Event}` span shape. Orleans grain calls propagate `Activity` context automatically, but **streams do not** — the subscriber handles an event on a later, detached continuation. The base `Event` class therefore carries W3C trace context (`TraceId`, `SpanId`, `TraceState`), captured at publish and used to stitch the handler span to the publishing span.

We deliberately model the cross-stream relationship as **parent-child**, not as an OTEL span **link**. Canonically, async fan-out delivery should be a link; we chose parent-child because links are poorly visualised in common tooling (Jaeger/Tempo) and engineers reading a trace expect to see the handler nested under the command that caused it.

## Consequences

- Traces are readable and intuitive at the cost of being slightly non-canonical for fan-out.
- A single slow handler does not inflate the parent command's own span duration (spans are stitched by context, the command does not await delivery).
- Dedup-suppressed redeliveries still emit a span tagged `edict.deduplicated=true` so dedup activity is visible rather than silent.
