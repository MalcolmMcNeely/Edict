# Transactional Outbox: one engine, stateful command handler, in-document atomicity

**Status:** accepted — amends ADR 0001 (lifts "no outbox, ever"), substantially amends ADR 0004 (`EdictCommandHandler` is no longer a stateless bare `Grain`), supersedes ADR 0012's "gap accepted until the Outbox" (the gap is now closed), supersedes the stop-gap `FlushRaisedEventsAsync` "publish-after-accept, throw on failure". Dead-lettering is split to ADR 0019 (read this first); the saga to ADR 0020.

A command handler that mutates state and then publishes events on a stream has a crash window: if the AKS pod dies between the state write and the publish, the event is lost while the state change stands (or vice versa). Orleans offers no cross-store transaction; the only atomic unit it gives is a single grain-state document write. We therefore make the durable side effect a **pending entry co-located in that one write**, drained at-least-once afterwards. Consumer idempotency (ADR 0002) already makes at-least-once effectively-once, so the Outbox does not attempt exactly-once publish.

The enabling reversal: `EdictCommandHandler` becomes the **stateful generic `EdictCommandHandler<TState>`**, persisting the envelope `{ TState aggregate, Outbox, DeadLetter }`. Without framework-owned durable aggregate state there is nothing for the outbox entry to be atomic *with* (the pre-existing sample held aggregate state in volatile grain fields — now invalid; it must move to `State`). The same engine serves `EdictIdempotencyBase<TPayload>` (event handlers, projections, sagas). One engine, three effect kinds: `PublishEvent` (→ domain stream), `SendCommand` (→ aggregate grain — the saga's path, ADR 0020), `UpsertRow` (→ Table Projection write store — this is how the ADR-0012 double-apply gap closes: the row write is an idempotent outbox effect committed atomically with the dedup-ring commit).

Drain is **FIFO, stop-at-head** (preserves per-aggregate causal order), **awaited inline immediately after the commit** (so the happy-path span tree is identical to the pre-Outbox code and ADR 0003's parent-child `Command → Publish → Handle` stitch is untouched — each entry carries the captured `traceparent`/`tracestate`, restored as the publish span's parent, never a span link). A **lazy Orleans Reminder** is the durable crash-recovery net: registered only when the outbox is non-empty, unregistered on full drain, plus drain-on-activation — so steady state holds **zero** reminders and only crashed/lagging grains carry one. An injected `TimeProvider` is the clock seam so the shipped in-memory Test Framework can virtualise backoff.

## Considered Options

- **Lean on Orleans for delivery durability (no outbox)** — rejected: Orleans streams are at-least-once but the *state-change↔publish* pair is not atomic; the pod-down requirement is exactly that gap.
- **Outbox as its own grain / its own store** — rejected: a second grain or store is a second non-atomic write, reintroducing the very gap (self-defeating). Atomicity *requires* co-location in the one grain document.
- **Keep `EdictCommandHandler` stateless; bind the outbox to a consumer-supplied state hook** — rejected: the framework cannot guarantee atomicity over a write it does not own; "takes care of it" demands the framework own the state document.
- **Reminder-only drain (no inline)** — rejected: every event would eat up-to-Reminder-period (~1 min Orleans floor) latency.
- **Standing reminder per grain** — rejected: unbounded reminder table / cost; lazy register-when-dirty scales to zero.
- **Span links instead of parent-child across the async gap** — rejected: contradicts ADR 0003's strict parent-child contract; parent-child kept, the wall-clock gap is deliberately invisible in the trace.

## Consequences

- ADR 0004 is amended: the command-handler base is generic and stateful; a `EdictCommandHandler : EdictCommandHandler<Unit>` shim is the stateless-handler convenience. The sample's volatile-field handlers must be rewritten onto `State`.
- ADR 0012's accepted double-apply limitation is **closed**, not deferred; `EdictTableProjectionBuilder` expresses its row write as a `UpsertRow` effect.
- `Send()` returns `Accepted` once `{State, Outbox}` is committed and the inline drain has been awaited; a publish failure after commit does **not** roll back and does **not** surface to the caller — the Reminder retries. "Accepted" means *durably accepted, will publish*.
- New folder `Outbox/` in `Edict.Core`; the engine is internal/bare-named (no consumer types it).
- Testing follows ADR 0016: drain/FIFO/backoff state-machine logic in `Edict.Core.Tests` (in-memory, virtual clock); the crash-across-pod redelivery+dedup proof (the ADR-0002-analogue) lives in `Edict.Azure.Tests` against Azurite via Testcontainers.

## Amendment — unified durable-consumer base and grain envelope (#55)

The host plumbing the two consumer-facing roots needed — the `IOutboxHost`
adapter, the lazy drain Reminder, the `IEdictDeadLetterAdmin` operator surface,
drain-on-activation, the `_drainReminderRegistered` bookkeeping, and the
intake-block guard (`EdictOutboxSaturatedException`) — was duplicated
byte-identically across `EdictCommandHandler<TState>` and
`EdictIdempotencyBase<TPayload>`. It is now collapsed onto a single internal
intermediate base **`EdictDurableConsumerBase<TPayload>`** (ADR 0017 clause (b)
outer shared root) and the two consumer-facing roots inherit it; the engine
seam (`OutboxDrainEngine`, `IOutboxHost`, the executors, `OutboxSlice`) is
untouched.

The persisted document shape is unified to **`GrainEnvelope<TPayload> {
Payload, Outbox, Idempotency }`** — the dedup state is a **sibling slot**,
not a wrapper around the payload. The wrapper type `IdempotencyPayload<T>` is
retired; the buffer field is renamed `IdempotencyState.Ring → HandledEventIds`
(the field name reflects ADR 0002's commit-after-success contract rather than
the circular-buffer implementation). Command Handlers simply never touch the
`Idempotency` slot; the cost is one empty `IdempotencyState` per envelope.
The frozen `[Alias("GrainEnvelope\`1")]` and `[Alias("IdempotencyState")]`
are preserved verbatim (ADR 0017 — persisted state must survive class
rename); `Head` and `Count` are unchanged.

Orleans grain-storage providers collapse from two (`edict-state` + `edict-dedup`)
to **one (`edict-state`)** — the persisted document is now one shape, so ops
configures one provider and one Azure Table. `edict-dedup` is removed from
`Edict.Azure` silo wiring and from `Edict.Testing`'s in-memory wiring.

## Amendment — composition refactor (#69)

Envelope ownership moves from inheritance to composition. The intermediate
`EdictDurableConsumerBase<TPayload>` that previously owned the persisted
`GrainEnvelope<TPayload>` document, the `IOutboxHost` adapter, the lazy
drain Reminder, and drain-on-activation is **deleted**. Its responsibilities
move into a bare-named, internal `OutboxHost<TPayload>` component (`Edict.Core/Outbox/OutboxHost.cs`)
that lives as a field on each consumer-facing root:

- `EdictCommandHandler<TState>` and `EdictIdempotencyBase<TPayload>` each
  derive from `Grain<GrainEnvelope<TPayload>>` directly (no intermediate
  base) and construct an `OutboxHost<TPayload>` lazily on first use, closed
  over a small persistent-state adapter (`GrainPersistentStateAdapter<T>`)
  bridging `base.State` + `WriteStateAsync` to the host's
  `IPersistentState<T>` seam.
- The standalone `OutboxDrainEngine` and the `IOutboxHost` interface are
  **deleted**; the drain algorithm (FIFO stop-at-head, exponential backoff,
  max-attempts dead-letter promotion via `IDeadLetterPromoter`) is folded
  into `OutboxHost<TPayload>` itself. With the host *being* the unit-testable
  thing, the engine/host split that existed only to serve the abstraction
  goes away.
- Reminder registration is the one residual coupling — Orleans's reminder
  API is grain-instance-bound. A tiny `IReminderRegistrar` adapter
  (`GrainReminderRegistrar`) closes over the hosting grain and is handed to
  the host at construction.
- Effect executors (`PublishEventExecutor`, `SendCommandExecutor`,
  `UpsertRowExecutor`, `InvokeHandlerExecutor`) no longer depend on
  `IOutboxHost`; their `ExecuteAsync` now takes the primitives an executor
  might need — `IStreamProvider` and the optional
  `Func<EdictEvent, Task>? deferredDispatch` callback — as explicit
  parameters, and each executor uses only what its effect requires.

The persisted `GrainEnvelope<TPayload>` shape and its frozen
`[Alias("GrainEnvelope\`1")]` are **unchanged** — state written before the
refactor is readable after, and no migration tooling is needed. The drain
algorithm, dedup-ring semantics, lazy-Reminder lifecycle, trace-context
capture/restore on staged entries, and outbox effect-kind enum are all
preserved byte-for-byte. The refactor is type-shape-only; every behavioural
test (Azurite redelivery + dead-letter end-to-end, telemetry span tree,
generator output for consumer subclasses, in-memory `Edict.Testing` Verify
timelines) passes unchanged.
