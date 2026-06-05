# EdictSchedule: in-grain durable scheduling

A Command Handler sometimes needs to do work on its own clock: poll an external system until it settles, advance a multi-step workflow one line at a time, or retry until a condition holds. Before EdictSchedule the only path was raw Orleans: call `RegisterGrainTimer` directly, then reach into the framework's `CommitAndDrainRaisedEventsAsync` escape hatch to persist the result. That path skips everything the dispatch lifecycle gives a normal handler — no throw-rollback to the last durable snapshot, no atomic state-plus-outbox commit, no trace nesting — and it sits right on top of the bug ADR-0055 fixed, because a self-scheduled callback that mutates state without raising an event used to lose that mutation on deactivation. The sample's Fulfillment aggregate was the live evidence: a raw grain timer with an apologetic docstring explaining that a Reminder's one-minute floor was too coarse for its sub-ten-second cadence, so it accepted losing durability across deactivation.

EdictSchedule is a first-class primitive for scheduling recurring work from inside a Command Handler. The consumer defines a serializable message, starts it from inside `HandleAsync` with one line, and writes one handler per fire that answers only "again or done".

```csharp
[Alias("fulfillment.fulfill-next-line")]
public sealed partial record FulfillNextLine : EdictScheduleMessage;

// inside HandleAsync(StartFulfillmentCommand command):
Schedule(new FulfillNextLine(), every: TimeSpan.FromSeconds(2));

// the fire handler:
public Task<EdictScheduleResult> HandleAsync(FulfillNextLine message)
{
    var pending = State.Lines.FindIndex(line => line.Status == Pending);
    if (pending < 0) { return Complete(); }
    State.Lines[pending] = State.Lines[pending] with { Status = Fulfilled };
    Raise(new LineItemFulfilledEvent(State.OrderId, State.Lines[pending].LineItemId));
    return Continue();
}
```

## The decision

- **A schedule carries a message, never a delegate.** A `Func` cannot be persisted, so a schedule persists the serializable `EdictScheduleMessage` (its data plus its `[Alias]` type identity). On fire, the framework deserializes it and routes it through the same generator-emitted dispatch type-switch already used for Commands, so the handler is resolved from the message type at compile time and the fired tick re-enters the full handler lifecycle. Anything a fire needs lives in durable `State` (read fresh each fire) or in the message itself.

- **No shared base; compose plus forward.** `EdictCommandHandler<TState>` has no common Edict base with `EdictSaga` (`Edict.Architecture.Tests` forbids one). Scheduling is delivered the same way the Outbox is (the composition refactor): a composed `ScheduleHost<TPayload>` field plus a thin `Schedule(...)` forward, added to `EdictCommandHandler` only.

- **`ScheduleSlice` is a pure deep module.** The durable collection of `ScheduleEntry` plus all timing transitions as pure functions over a passed-in `now`: add an entry, compute which entries are due, coalesce multiple elapsed periods into a single fire, and apply a `Continue` / `Complete` outcome to produce the next due instant. No I/O and no clock dependency beyond `now`. It mirrors `OutboxSlice`, and is where the timing semantics are exhaustively unit-tested.

- **`ScheduleSlice` rides the grain envelope as a fourth slot.** The single-write persisted document becomes `{ Payload, Outbox, Idempotency, Saga, Schedule }`, default-empty on grains that never schedule, so a schedule entry commits atomically with state and outbox in one write — a fire's state mutation, its raised events, and the schedule's next-due advance all land together.

- **A hybrid clock: grain timer plus Reminder.** `ScheduleHost` arms a non-keepalive grain timer for sub-minute precision while a schedule is active, and the `edict-schedule` Reminder as the one-minute durability backstop. The timer is `KeepAlive = false` deliberately: a schedule must survive deactivation, so the grain is allowed to idle out and the durable Reminder re-activates it to catch up. Both arm against the injected framework `TimeProvider`, not wall-clock, so the in-memory Test Framework's virtual clock can drive firing. The host never writes the slice itself — `Schedule(...)` stages it in memory and the enclosing command's commit makes it durable; a fire's next-due advance rides the grain's own `CommitAndDrainRaisedEventsAsync`.

- **The schedule dispatch lifecycle is hand-written in `Edict.Core`.** A method parallel to `ValidateAndHandleAsync`: deserialize the fired message, route it through the generated dispatch type-switch, run the fire handler, apply the returned `EdictScheduleResult` to the slice, commit and drain through the existing `CommitAndDrainRaisedEventsAsync`. Hand-written rather than generator-emitted so it is unit-testable without the generator — the same reasoning ADR-0055 applied when it moved the command lifecycle out of the spine.

- **Cadence is declared once; the handler answers only `Continue` / `Complete`.** `Schedule(EdictScheduleMessage message, TimeSpan every)`, first fire at `+every`. There is no jitter and no second cadence knob. v1 is recurring-only; a one-shot is `every: X` plus `Complete()` on the first fire.

- **Missed ticks coalesce into a single fire.** A grain that slept through many periods fires once on catch-up and resumes on the original cadence grid (the next instant strictly after `now`), rather than replaying a backlog of catch-up fires.

- **A fire that throws rolls back and re-times forward.** A handler throw discards the turn — buffered events dropped, the partial `State` mutation rolled back to the last durable snapshot — then re-times the schedule forward by one cadence so a failing fire retries on the next period rather than spinning the timer. There is no dead-letter path for a stuck schedule in this slice; see the timeout section below.

## Timeout model (deferred)

A schedule that runs too long needs a cap, mirroring the saga's absolute lifetime cap (ADR-0050): a fixed deadline armed once at registration and never reset by ticks, with a finite silo-wide default (7 days, name-identical to `EdictSagaOptions.DefaultTimeout`), a per-schedule `timeout:` override, an `EdictSchedule.Unbounded` opt-out, and an `OnScheduleTimeoutAsync` compensation hook that dead-letters by default when absent. This slice deliberately ships without it: a schedule runs until `Complete()`. The cap, its configuration, and the loud-failure path land in the next slice so this tracer bullet stays thin.

## Considered options

- **Generator-emit the fire lifecycle (like the command spine once did).** Rejected for the same reason ADR-0055 moved the command lifecycle to hand-written `Edict.Core`: a hand-written lifecycle is unit-testable without standing up the generator, and the fire path is where throw-rollback and atomic-commit correctness matters most.

- **Reminder-only, no grain timer.** Rejected: the Orleans Reminder floor is one minute, too coarse for the sub-ten-second cadences the primitive exists to serve. The grain timer carries sub-minute precision; the Reminder is only the durability backstop.

- **`KeepAlive = true` on the grain timer (as the raw-timer escape hatch used).** Rejected: pinning the grain alive defeats the durability the primitive exists to give. A schedule must survive deactivation, which means letting the grain idle out and relying on the durable Reminder to re-activate and catch up.

- **A separate per-schedule registry the host reports to, so the Test Framework can discover due schedules.** Rejected: a schedule is only ever started from inside a Command's `HandleAsync`, so the recording sender has already routed a Command to every grain that could hold one. The Test Framework's `FireDueSchedulesAsync` discovers grains from that roster and peeks each via a framework-internal grain method, so no production-side registry exists purely to serve tests.

## Consequences

- A new consumer surface: `EdictScheduleMessage` (the serializable base, on the public-surface allow-list), `EdictScheduleResult` with `Continue` / `Complete` cases, `Schedule(message, every)`, and the `Continue()` / `Complete()` result helpers. The generator emits a `DispatchScheduleFireAsync` type-switch parallel to the command spine; an un-annotated grain emits nothing.

- `GrainEnvelope<TPayload>` gains an `[Id(4)] Schedule` slot, changing the persisted shape and forcing a Verify snapshot regen. No released-consumer compatibility constraint applies (pre-release).

- The Test Framework gains `EdictTestApp.FireDueSchedulesAsync()`: interval-agnostic, it reads the soonest due instant across routed grains from durable state, advances the virtual clock to it, fires the due grains, and drains so the outcome lands on the `Timeline`. Chainable, for walking a multi-step scheduled workflow deterministically.

This ADR records the decision and rationale. ADR-0055 defines the completion-based state-persistence guarantee a fire inherits; ADR-0050 defines the saga lifetime cap the deferred schedule timeout will mirror.
