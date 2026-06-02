# Core configuration

The provider-agnostic knobs, configured through `AddEdict(...)`. These tune mechanisms that run on every pairing — the outbox, at-least-once dedup, and saga timeouts — so they live here rather than on a streaming or persistence page. Two options classes share the `AddEdict()` bucket: `EdictOptions` and `EdictSagaOptions`.

```csharp
silo.AddEdict(options =>
{
    options.IdempotencyWindowSize = 256;
    options.OutboxMaxAttempts     = 12;
});
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

`DefaultTimeout` is a `TimeSpan?` configured under the same `AddEdict()` call, sibling to `EdictOptions`. See [sagas.md](../usage/concepts/sagas.md) for how the cap fires and the compensation hook it dispatches.

## See also

- [index.md](index.md) — the installation surface and fail-fast validation behaviour.
- [getting-started.md](../usage/getting-started.md) — the supported pairing matrix.
- Concepts — [idempotency.md](../usage/concepts/idempotency.md), [events.md](../usage/concepts/events.md), [dead-letter.md](../usage/concepts/dead-letter.md), [sagas.md](../usage/concepts/sagas.md).
- ADRs — [0023 — Config surface and installation](../adr/0023-config-surface-and-installation.md), [0050 — Saga absolute lifetime cap](../adr/0050-saga-absolute-lifetime-cap.md).
