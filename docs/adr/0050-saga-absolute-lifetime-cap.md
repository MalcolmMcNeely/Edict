# Saga absolute lifetime cap

A saga waits for events that may never arrive. The payment authoriser never calls back; the shipping partner drops the order; an upstream aggregate stops raising. Without a deadline, the saga grain and its progress sit live forever, holding a reminder and a slot in every observability rollup, with no signal that the workflow has stalled. Edict gives every saga a deadline so a stalled workflow becomes visible and bounded instead of silently immortal.

The cap is **absolute**, not per-step and not idle-based. It is armed once, when the saga handles its first Event, and is **never reset** by later activity. The deadline is "this saga started at T and must finish by T + cap": nothing the saga does afterward moves it.

The framework does not model a saga's step graph. A saga is a set of `HandleAsync` overloads reacting to events; there is no declared sequence the framework could attach a per-step timer to. An idle cap (reset on every handled event) cannot bound a saga that keeps receiving partial activity but never finishes. The absolute cap is the only deadline computable with zero consumer ceremony from the one fact the framework owns (when the saga started), and a coarse "this workflow has been alive too long" bound is the right shape for a safety net rather than a scheduler.

## The decision

- **Absolute lifetime cap, armed on first handle, never reset.** `SagaLifecycle.DeadlineAt` is set on the first handled Event and left untouched by subsequent handles. The cap is enforced by a per-grain Orleans reminder named `edict-saga-cap`.

- **A shipped finite default (7 days) with per-saga opt-out.** `EdictSagaOptions.DefaultTimeout` ships finite at 7 days. A saga overrides the cap with `[EdictSagaTimeout("d.hh:mm:ss")]`, or opts out entirely with `[EdictSagaTimeout(Unbounded = true)]`, which beats even a finite silo-wide default. The default is finite, not unbounded, so a stalled saga cannot leak a reminder and a live activation forever by accident; going unbounded is an explicit, greppable choice on the saga type.

- **`Complete()` is hard-terminal.** A saga marks itself successfully finished by calling `Complete()` from a handler. The lifecycle moves to `Completed` in the same atomic write as the handler's progress and any dispatched Command, and the cap reminder is unregistered. `Complete()` is opt-in: a saga whose key may legitimately receive a later Event (a long-lived coordinator) simply never calls it and relies on the absolute cap as its only terminal path.

- **A genuinely-new Event the saga handles dead-letters at a terminal saga.** Once a saga is terminal (`Completed` via `Complete()` or `TimedOut` via the cap), a genuinely-new Event (a new `EventId`, not an at-least-once redelivery, which the dedup ring still suppresses) for a type the saga has a `HandleAsync` overload for is recorded as a dead letter carrying `EdictSagaTerminalException`, and the lifecycle stays terminal. Silently re-handling a late Event would resurrect a finished workflow and re-dispatch its Commands; dead-lettering makes the violation visible on the forensic surface instead. The guard is type-aware on purpose: Orleans implicit subscriptions are per-stream and a domain stream carries many event types, so a saga routinely receives types it does not handle. Those are ignored at a terminal saga exactly as they are ignored while it is live: only handled types dead-letter, so completing a saga that shares a stream does not flood the dead-letter projection with unrelated traffic.

- **A fired cap dead-letters by default; override `OnSagaTimeoutAsync()` to compensate.** When the cap fires on a live saga, the framework gives it one chance to compensate. The default `OnSagaTimeoutAsync()` routes the fired cap to dead-letter (`EdictSagaTimeoutException`), so a forgotten timeout surfaces loudly rather than silently stranding the workflow. An override may mutate `Progress` and `Dispatch` exactly one compensating Command, which commits atomically with the `TimedOut` terminal write. Dead-letter is the default because a finite-capped saga that times out without a compensation path is almost always a bug (a workflow that should have completed did not), and the safe behaviour is to make that bug visible, not to discard it.

## Considered Options

- **Per-step deadlines with automatic compensation** (the original "What's next" framing). Rejected: the framework has no step graph to hang per-step timers on. A saga is event handlers, not a declared sequence; modelling steps would force consumers to declare a workflow DSL the rest of Edict deliberately avoids. The absolute cap delivers the safety-net guarantee (no saga lives forever) without that ceremony.

- **Idle cap (reset the deadline on every handled Event).** Rejected: an idle cap cannot bound a saga that keeps receiving some activity but never reaches a terminal state, which is exactly the stuck-coordinator shape the cap exists to catch.

- **Unbounded by default, finite only when annotated.** Rejected: the failure mode of an unbounded default is silent and unbounded: a stalled saga leaks a reminder and a live activation indefinitely, and nothing in the code signals it. A finite default inverts that: "lives forever" requires an explicit `Unbounded = true` on the type. 7 days is long enough that no healthy workflow reaches it and short enough to bound the leak from one that stalls.

- **Silently discard or auto-retry a fired cap.** Rejected. Discarding hides the bug; auto-retry has no defined semantics for a coordinator that already dispatched partial Commands. Routing to dead-letter by default keeps the forensic surface as the single place a stalled workflow shows up, consistent with ADR-0018.

- **A dedicated cap reminder period knob.** Rejected: the cap reuses `EdictOptions.OutboxDrainReminderPeriod` (already validated at or above the Orleans one-minute reminder floor) as both its period and its due-time floor. The cap is a coarse absolute bound, so reminder granularity of a minute is immaterial, and a new knob would be configuration surface no consumer needs to reason about.

## Consequences

- `[EdictSagaTimeout]` takes a `TimeSpan`-parseable literal. The leading field is **days**, so `"24:00:00"` parses as **24 days**, not 24 hours; write one day as `"1.00:00:00"`. Analyzers EDICT020 (the literal must parse to a positive `TimeSpan`), EDICT021 (`Duration` and `Unbounded` are mutually exclusive), and EDICT022 (an `OnSagaTimeoutAsync` override on an `Unbounded` saga is dead code) guard the declaration at build time.

- Two lifecycle counters land on the `"Edict"` Meter: `edict.saga.timeout.fired` (tagged `compensated` or `deadlettered`) and `edict.saga.completed`. The ratio `fired / (fired + completed)` separates a handful of timeouts among millions from a rising failure trend. The existing `edict.saga.progress.age` gauge stays as the leading indicator of a saga approaching its cap.

- `EdictSagaTimeoutException` and `EdictSagaTerminalException` join the dead-letter failure-reason allowlist as `SagaTimeout` and `SagaTerminal`, distinct from `ConsumerBug`, so the two saga-lifecycle failures read apart on the dead-letter projection.

- The cap arms and terminalises identically on the inline stream path and the deferred claim-check (pointer-envelope) path, so an oversized first saga Event still arms the cap and an oversized Event at a terminal saga still dead-letters.

- The lifecycle is the persisted `SagaLifecycle` slot on the grain envelope (`[Id(3)]`, null for non-saga consumers), so a crash mid-workflow recovers the deadline and terminal state with the rest of grain state.

This ADR records the decision and rationale. ADR-0016 defines the saga model itself (one Command per Event via `Dispatch`); this ADR adds the lifecycle that bounds it.
