# Schedules

A Command Handler sometimes needs to do work on its own clock: poll an external system until it settles, advance a multi-step workflow one line at a time, or retry until a condition holds. `EdictSchedule` is the first-class way to run that recurring work. The consumer defines a serializable message, starts the schedule from inside `HandleAsync` with one line, and writes one fire handler that answers only "again or done".

```csharp
using Edict.Contracts.Schedules;
using Edict.Core.Commands;

[Alias("fulfillment.fulfill-next-line")]
public sealed partial record FulfillNextLine : EdictScheduleMessage;

public partial class FulfillmentCommandHandler : EdictCommandHandler<FulfillmentState>
{
    Task<EdictCommandResult> HandleAsync(StartFulfillmentCommand command)
    {
        State.OrderId = command.OrderId;
        State.Lines = command.LineItemIds
            .Select(id => new FulfillmentLine { LineItemId = id, Status = LineItemFulfillmentStatus.Pending })
            .ToList();

        Schedule(new FulfillNextLine(), every: TimeSpan.FromSeconds(2));
        return Task.FromResult<EdictCommandResult>(new EdictCommandResult.Accepted());
    }

    async Task<EdictScheduleResult> HandleAsync(FulfillNextLine message)
    {
        var pendingIndex = State.Lines.FindIndex(line => line.Status == LineItemFulfillmentStatus.Pending);
        if (pendingIndex < 0)
        {
            return await Complete();
        }

        var line = State.Lines[pendingIndex];
        State.Lines[pendingIndex] = line with { Status = LineItemFulfillmentStatus.Fulfilled };
        Raise(new LineItemFulfilledEvent(State.OrderId, line.LineItemId));

        return await Continue();
    }
}
```

## Surface

- **`EdictScheduleMessage`** (`Edict.Contracts.Schedules`) is the base for the serializable message a schedule carries. A concrete message is a `record` deriving from it, carrying only its own domain data, with `[Alias("literal")]` for rename-survival. A schedule persists this message, never a delegate, so it survives grain deactivation.
- **`Schedule(EdictScheduleMessage message, TimeSpan every, TimeSpan? timeout = null)`** (`protected void`) starts a recurring schedule from inside `HandleAsync`. The first fire lands at `+every`, then every cadence after. The staged schedule is durable because it commits in the same grain-state write as the handler's `State` and any raised events. `timeout:` is covered under [Timeout semantics](#timeout-semantics) below.
- **`HandleAsync(TMessage message) : Task<EdictScheduleResult>`** is the fire handler the consumer writes, one per schedule-message type. Each fire deserializes the message and routes it back through the handler's generated dispatch, re-entering the full handler lifecycle: it reads fresh `State`, may `Raise` events, and commits atomically. The return value answers only whether to fire again.
- **`EdictScheduleResult`** (`Edict.Contracts.Schedules`) is a closed hierarchy of exactly `Continue` (fire again on the declared cadence) and `Complete` (stop the schedule). On a Command Handler, return the helpers `Continue()` / `Complete()` (both `protected static Task<EdictScheduleResult>`). The handler answers "again or done"; the cadence lives at the `Schedule(...)` call site, never in the result.
- **`OnScheduleTimeoutAsync(TMessage message)`** is the optional compensation hook the framework invokes when the timeout cap fires. Override it to compensate; without it, a fired cap dead-letters. See [Timeout semantics](#timeout-semantics).

A schedule message is dispatched only by the grain that armed it: the framework resolves "which handler" from the message type at compile time through the same generator-emitted dispatch switch used for Commands, so a fired tick gets throw-rollback, atomic state-plus-outbox commit, and trace nesting exactly like any other handler.

## Durability model

A schedule is durable, not a timer that dies with the grain. Three properties make that work.

**It carries a message, not a delegate.** A `Func` cannot be persisted, so the durable `ScheduleEntry` holds the serialized `EdictScheduleMessage` (its data plus its `[Alias]` identity), its cadence, its deadline, and its status. Everything a fire needs lives either in that message or in durable `State`, read fresh each fire. This is why a fire handler never closes over a captured variable: the only inputs that survive a deactivation are the message and `State`.

**It survives deactivation and catches up on reactivation.** The clock is hybrid: a grain timer carries sub-minute precision while the grain is active, and the `edict-schedule` Reminder is the one-minute durability backstop. The timer is deliberately non-keepalive, so the grain is allowed to idle out; the durable Reminder re-activates it and the schedule resumes. A grain that slept through several periods does not replay a backlog: missed ticks coalesce into a single catch-up fire, after which the schedule resumes on its original cadence grid (the next instant strictly after now).

**A fire that throws rolls back and re-times forward.** A throwing fire discards the turn: buffered events are dropped and the partial `State` mutation rolls back to the last durable snapshot. The schedule then re-times forward by one cadence, so a transient failure retries on the next period rather than spinning the timer or dead-lettering. A single throwing fire is never a dead-letter; the timeout cap below is the backstop for a schedule that stays stuck.

This is the durability the old escape hatch could not give. Before `EdictSchedule`, the only path was a raw `RegisterGrainTimer` plus the `CommitAndDrainRaisedEventsAsync` internal, which skipped throw-rollback and atomic commit and lost a self-scheduled state mutation across deactivation.

## Timeout semantics

Every schedule carries an absolute timeout cap, mirroring the [saga lifetime cap](sagas.md#lifecycle-the-absolute-cap). The cap is a `DeadlineAt` armed once when the schedule is registered and **never reset by a fire**, so a healthy recurring schedule cannot push its own deadline forever and defeat the safety net.

**The default is finite.** `EdictCommandHandlerScheduleOptions.DefaultTimeout` ships at 7 days, so no Command Handler schedule can tick forever by accident. Resolution precedence at the `Schedule(...)` call site:

```csharp
Schedule(new WatchdogPoll(),    every: cadence, timeout: TimeSpan.FromMinutes(5)); // explicit cap wins
Schedule(new RenewLeaseMessage(), every: cadence, timeout: EdictSchedule.Unbounded); // opt out of any cap
Schedule(new FulfillNextLine(), every: cadence);                                   // inherit the silo default
```

An explicit positive `timeout:` wins. `EdictSchedule.Unbounded` (a branded sentinel `TimeSpan`, distinct from `null`) opts a legitimately perpetual schedule out of any cap, so "forever" is always a deliberate, visible choice. Omitting `timeout:` inherits `EdictCommandHandlerScheduleOptions.DefaultTimeout`; setting that option to `null` (or `Unbounded`) returns the silo to uncapped command-handler schedules. The option is configured through the `configureSchedule` lambda on `AddEdict()`; see [core configuration](../../configuration/core.md#edictcommandhandlerscheduleoptions).

**When the cap fires**, the framework gives the schedule one chance to compensate through `OnScheduleTimeoutAsync(TMessage)`:

```csharp
Task OnScheduleTimeoutAsync(WatchdogPoll message)
{
    Raise(new WatchdogEscalatedEvent(State.WatchdogId, State.Polls));
    return Task.CompletedTask;
}
```

The override may mutate `State` and `Raise` events; both commit atomically with the schedule's removal. **Without an override, a fired cap dead-letters** with the `EdictScheduleTimeoutException` marker, so a capped schedule that times out with no compensation path surfaces loudly on the [dead-letter projection](dead-letter.md) rather than silently stranding the work.

## Command Handler and Saga parity

The scheduling surface on an `EdictSaga` is identical: a saga schedules a recurring message from inside an event `HandleAsync`, writes the same `HandleAsync(TMessage) : Task<EdictScheduleResult>` fire handler, and may write the same `OnScheduleTimeoutAsync(TMessage)` compensation hook. A saga fire dispatches its work through `Dispatch` (the saga's single-command-per-fire effect) instead of `Raise`.

```csharp
public partial class GatewaySettlementSaga : EdictSaga<GatewaySettlementProgress>
{
    Task HandleAsync(GatewaySettlementBegunEvent edictEvent)
    {
        Progress.PaymentId = edictEvent.PaymentId;
        Schedule(new PollGatewayMessage(), every: TimeSpan.FromMinutes(5));
        return Task.CompletedTask;
    }

    Task<EdictScheduleResult> HandleAsync(PollGatewayMessage message)
    {
        Progress.Polls++;
        if (Progress.Polls >= 2)
        {
            Dispatch(new ConfirmSettlementCommand(Progress.PaymentId));
            return Task.FromResult<EdictScheduleResult>(new EdictScheduleResult.Complete());
        }

        return Task.FromResult<EdictScheduleResult>(new EdictScheduleResult.Continue());
    }
}
```

Two differences are worth noting:

- **The result helpers.** A saga returns `new EdictScheduleResult.Continue()` / `new EdictScheduleResult.Complete()` directly. The `Continue()` / `Complete()` helper methods exist only on `EdictCommandHandler`, because `EdictSaga` already has a lifecycle `Complete()` (the void terminal-success method from [sagas.md](sagas.md)) and overloading it would collide.
- **Which cap applies.** A saga's schedule is bounded by the **saga's own lifetime cap** (`[EdictSagaTimeout]` or the saga default), never by `EdictCommandHandlerScheduleOptions.DefaultTimeout`. A saga schedule with no explicit `timeout:` inherits the saga cap; an explicit `timeout:` caps it shorter; `EdictSchedule.Unbounded` opts the schedule out so only the saga's own lifetime bounds it. The command-handler schedule default never reaches a saga.

## Testing

The in-memory [Test Framework](../testing/seams.md) drives schedules through a virtual clock, with no real timers. After a command arms a schedule, advance it interval-agnostically:

```csharp
await app.FireDueSchedulesAsync();      // advance to the next due tick across routed grains, fire, drain
await app.FireScheduleTimeoutsAsync();  // advance to the next cap, fire the timeout, drain
```

Both read the soonest due (or cap) instant across routed grains from durable state, advance the clock to it, fire, and drain so the outcome lands on the `Timeline`. They are chainable, so a multi-step scheduled workflow is walked one fire at a time without ever naming the literal cadence in the test.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `EdictSchedule`, `Saga`, `Saga Timeout`, `EdictCommand`, `Event`.
- Concepts — [commands.md](commands.md), [events.md](events.md), [sagas.md](sagas.md), [dead-letter.md](dead-letter.md), [idempotency.md](idempotency.md).
- Configuration — [core.md](../../configuration/core.md#edictcommandhandlerscheduleoptions).
- ADRs — [0056 — EdictSchedule in-grain durable scheduling](../../adr/0056-edict-schedule-in-grain-durable-scheduling.md), [0050 — Saga absolute lifetime cap](../../adr/0050-saga-absolute-lifetime-cap.md), [0055 — Command Handler state persists on completion](../../adr/0055-command-handler-state-persists-on-completion.md).
