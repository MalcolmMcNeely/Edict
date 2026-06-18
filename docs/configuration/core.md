# Core configuration

The provider-agnostic knobs, configured through `AddEdict(...)`. These tune mechanisms that run on every pairing — the outbox, at-least-once dedup, saga timeouts, and command-handler schedule timeouts — so they live here rather than on a streaming or persistence page. Three options classes share the `AddEdict()` bucket: `EdictOptions`, `EdictSagaOptions`, and `EdictCommandHandlerScheduleOptions`.

```csharp
silo.AddEdict(
    options =>
    {
        options.IdempotencyWindowSize = 256;
        options.OutboxMaxAttempts     = 12;
    },
    configureSaga: saga => saga.DefaultTimeout = TimeSpan.FromDays(3),
    configureSchedule: schedule => schedule.DefaultTimeout = TimeSpan.FromDays(3));
```

## `EdictOptions`

| Property | Default | Purpose |
| --- | --- | --- |
| `IdempotencyWindowSize` | `100` | Silo-wide default for the number of distinct `EventId` values each consumer remembers for at-least-once redelivery dedup. A high-throughput grain type can override this — see the per-grain note below. |
| `OutboxBaseDelay` | `2 s` | First outbox retry delay; doubles each attempt up to `OutboxMaxDelay`. Must be greater than zero and no larger than `OutboxMaxDelay`, or startup fails. |
| `OutboxMaxDelay` | `5 min` | Backoff ceiling. A long outage cannot push the next retry attempt past this. |
| `OutboxMaxAttempts` | `8` | Attempts before a permanently failing outbox entry is promoted to a dead-letter publish. The attempt that reaches this count removes the entry and appends an `EdictDeadLetterRaised` publish at the FIFO tail in one write. Must be at least 1. |
| `OutboxJitterFraction` | `0.2` | Fraction of the computed delay used as a deterministic ±spread per entry, so a fleet failing together does not stampede the same retry instant. `0` disables jitter. Validated to `[0, 1]`; an out-of-range value throws at startup (no silent clamp). |
| `OutboxDrainReminderPeriod` | `1 min` | Period of the lazy outbox drain reminder. Orleans' reminder floor is one minute; a value below that throws at startup. |
| `CorrelationWindowSize` | `100` | Number of distinct `ConversationId` values a Projection remembers for read-your-writes. Distinct from `IdempotencyWindowSize`, so a Projection that never reads with a cursor pays nothing and the two windows tune independently. A cursor for a conversation that has aged out of this window degrades to a plain read. Must be at least 1. |
| `ProjectionReadTimeout` | `2 s` | Bounded default a read-your-writes Projection read waits when the caller supplies a cursor but no explicit timeout. An omitted timeout falls back to this bound and never to an infinite wait; pass `Timeout.InfiniteTimeSpan` explicitly to wait indefinitely. Must be greater than zero, so the default can never be infinite. |
| `ReminderRegistrationRetryCount` | `3` | Total attempts (one try plus retries) the reminder registrar makes when Orleans' reminder service is still initializing during silo cold-start. Every durability backstop — outbox drain, schedule reactivation, saga lifetime cap, audit drain — arms through the one registrar, which retries the transient "still initializing" fault and only fails loud once the budget is exhausted. Widen it for a cluster whose reminder service is slow to start. The common path (service already started) pays nothing. Must be at least 1. |

### Per-grain `WindowSize` override

`IdempotencyWindowSize` is the silo-wide floor. A specific consumer that processes a high event rate can run a larger dedup ring by overriding `WindowSize` on the grain type:

```csharp
protected override int WindowSize => 4096;
```

The override reads once per activation and caches the value. Leave it unset and the grain inherits `EdictOptions.IdempotencyWindowSize`. See [idempotency.md](../usage/concepts/idempotency.md) for the mechanism.

## `EdictSagaOptions`

| Property | Default | Purpose |
| --- | --- | --- |
| `DefaultTimeout` | `7 days` | Absolute lifetime cap applied to any saga that does not declare its own `[EdictSagaTimeout]`. Ships finite so no saga can sit forever by accident, even one nobody annotated. Set to `null` to return the silo to fully opt-in behaviour — no saga is capped unless it carries the attribute. A per-saga `[EdictSagaTimeout(Unbounded = true)]` always beats this default; opting out is the saga author's call. |

`EdictSagaOptions.DefaultTimeout` is a `TimeSpan?` configured via the `configureSaga` lambda on `AddEdict()`, sibling to `EdictOptions`. See [sagas.md](../usage/concepts/sagas.md) for how the cap fires and the compensation hook it dispatches.

## `EdictCommandHandlerScheduleOptions`

| Property | Default | Purpose |
| --- | --- | --- |
| `DefaultTimeout` | `7 days` | Timeout cap applied to any Command Handler schedule started without an explicit `timeout:`. Ships finite (matching `EdictSagaOptions.DefaultTimeout`) so no schedule can tick forever by accident. Set to `null` to return the silo to uncapped command-handler schedules — a schedule then runs until it `Complete()`s or carries its own `timeout:`. A per-schedule `timeout: EdictSchedule.Unbounded` always beats this default. |

`EdictCommandHandlerScheduleOptions.DefaultTimeout` is a `TimeSpan?` configured via the `configureSchedule` lambda on `AddEdict()`. It is distinct from `EdictSagaOptions.DefaultTimeout`: this cap governs schedules a Command Handler arms, while the saga cap governs a saga's whole lifetime (and a saga's own schedules inherit the saga cap, not this one). See [ADR 0056](../adr/0056-edict-schedule-in-grain-durable-scheduling.md) for the timeout model and the `EdictSchedule.Unbounded` opt-out.

## See also

- [index.md](index.md) — the installation surface and fail-fast validation behaviour.
- [getting-started.md](../usage/getting-started.md) — the supported pairing matrix.
- Concepts — [idempotency.md](../usage/concepts/idempotency.md), [events.md](../usage/concepts/events.md), [dead-letter.md](../usage/concepts/dead-letter.md), [sagas.md](../usage/concepts/sagas.md).
- ADRs — [0023 — Config surface and installation](../adr/0023-config-surface-and-installation.md), [0050 — Saga absolute lifetime cap](../adr/0050-saga-absolute-lifetime-cap.md).
