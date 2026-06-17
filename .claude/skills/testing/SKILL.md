---
name: testing
description: Use this skill when creating, modifying, or reviewing tests in the Edict repo — xUnit, Verify snapshots, Testcontainers/Azurite, generator & analyzer tests. Covers the ADR 0016 project layering, naming, Verify path rules, and what not to do.
---

# Test Philosophy (Edict)

## Guiding principle

Test external behaviour, not implementation details. A test should survive a rename or refactor that does not change observable behaviour. If it breaks on a pure rename, it was written at the wrong level.

## Project layering (ADR 0016)

Each suite has a single, non-overlapping job. Putting a test in the wrong project is the most common mistake here.

| Project | What it tests | Backend |
|---|---|---|
| `Edict.Core.Tests` | Mechanism *logic*: dedup-ring semantics, projection orchestration, command routing | In-memory streams/stores. **No Testcontainers** — fast inner loop. Reaching for Azurite here is a smell. |
| `Edict.Azure.Streaming.Tests` / `Edict.Kafka.Tests` | Streaming-axis conformance battery: real broker + reference persistence — at-least-once redelivery, dedup realism (the ADR-0002 proof), span stitch across the hop | **real AQS/Kafka via Testcontainers** (ADR-0054) |
| `Edict.Azure.Persistence.Tests` / `Edict.Postgres.Tests` | Persistence-axis conformance battery: real store + dumb `MemoryStreams` — outbox atomicity, table-projection persistence, dead-letter rows | **real Azure Table/Blob or Postgres via Testcontainers** (ADR-0054) |
| `Edict.Telemetry.Tests` | Span tree + `edict.*` tags | `ActivityListener` |
| `Edict.Generators.Tests` | Generator output shape | Verify snapshots of emitted source |
| `Edict.Analyzers.Tests` | `EDICT00x` diagnostic coverage | analyzer test harness; assert diagnostic **line** positions |
| `Edict.Architecture.Tests` | `BoundaryTests`, `TypePlacementTests` | reflection over assemblies |

The shipped Test Framework (`Edict.Testing`) is the *only* place in-memory wiring is correct for consumer-facing scenarios. The Sample app never uses in-memory infra.

## Test naming

`Subject_Should{Outcome}[_When{Condition}]`.

- `Subject` is the method under test when one exists, else a scenario noun (`EDICT001`, `CommandPipeline`, `ClosedHierarchy`).
- `_When{Condition}` only when there *is* a condition — drop it for unconditional facts.
- Examples: `Send_ShouldReturnRejected_WhenValidatorFails`, `EDICT001_ShouldNotRaise_WhenGrainIsPartial`, `CommandResult_ShouldBeClosedHierarchy`.

Structure every test Arrange / Act / Assert. The `// Arrange`, `// Act`, `// Assert` markers are a permitted readability convention in test bodies — they are the one exception to the general "no comments that restate what the code does" rule.

## Verify

| Purpose | Library |
|---|---|
| Test framework | **xUnit** |
| Assertions | **xUnit built-ins** (`Assert.*`) |
| Snapshot | **Verify** (`Verify.Xunit`) |
| Containers | **Testcontainers** |

- Use **Verify** when a return value has more than one field to assert. Don't write `Assert.Equal` chains and add Verify later — use it on first write.
- Verify scrubs Guids/DateTimes by default (`Guid_1`, `DateTime_1`). Do **not** add `.IgnoreMembersWithType<Guid>()` — ignoring removes the field from the snapshot so its existence is no longer verified. Let default scrubbing work; use `DontScrubGuids()` only when raw values matter.
- If a Guid is semantically load-bearing (ownership, FK link), assert it separately with `Assert.Equal` alongside the `Verify(...)`.
- Snapshots live in a **flat `{TestProject}/Snapshots/` directory** — a `ModuleInitializer` sets `Verifier.DerivePathInfo` so deep folder nesting never eats the Windows path budget. Contributors run `git config core.longpaths true` once.
- **Soft length cap:** if `{Class}.{Method}` would push a `.verified.txt` filename past ~90 chars, the test scope is too broad — split the test. Never truncate or hash snapshot filenames (they must stay greppable and rename-stable).
- Never commit `.received.*` files — only `.verified.*`.

## Metrics (`MeterListener`)

A `MeterListener` is **process-global**: it observes every emit of the named instrument anywhere in the process, on whatever thread emits it. How you capture depends on where the emit runs.

| Emit context | How to capture | Why |
|---|---|---|
| **Synchronous on the test thread** — system-under-test newed up as a plain object and driven inline (e.g. `OutboxDrainMetricsTests` constructs `OutboxHost` directly; `ClaimCheckPolicyMetricsTests` constructs `ClaimCheckPolicy`) | Plain `List<T>`, assert immediately | The measurement fires inline before the act returns — no second thread, no race. **Prefer this**: drive the emit through a directly-constructed unit, not a cluster. |
| **Observable gauge** (`CreateObservableGauge`, ADR 0040) | Set state, call `listener.RecordObservableInstruments()`, then assert | The pull runs the gauge callback on the test thread, so you control exactly when the measurement is taken. |
| **Push counter/histogram emitted inside a grain** in an in-process `TestCluster` (cross-thread) | Lock-guarded sink **and** a bounded poll for the expected capture — never an immediate `Assert.Single` on a plain `List` | The instrument fires on the grain-activation thread; the measurement can land a scheduler tick after the awaited grain call returns. Reading the list once races — this is the bug that bit `SagaLifecycleMetricsTests`. A push emit that is fully linearised by an awaited grain call (e.g. `IEdictSender.SendAsync`, a direct `DispatchAsync`) is the one cross-thread case where an immediate assert is safe, because the reply establishes happens-before; lock-guard the sink anyway. |

Two rules regardless of shape:

- **Filter captures by a per-test marker** — the `GrainType` tag, or a unique GUID baked into the instrument's tags. The process-global listener will otherwise see emits from parallel test classes and fail with "more than one element."
- If sibling classes emit the *same* instrument, bind them into one `[Collection]` so they run serially. Serialisation prevents cross-class capture pollution; it does **not** substitute for the bounded poll, which guards the test's *own* emit landing in time.

## Spans (`ActivityListener`)

`ActivityStopped` is the span counterpart of the `MeterListener` race, and the same discipline applies. The callback fires when a span's `using`-scope unwinds, process-globally, on whatever thread the span ran.

**Wait on the span you assert on — never on a proxy signal plus a fixed delay.** A deferred span (`edict.event.handle`, a saga-dispatched `edict.command.handle`, `edict.schedule.fire`) stops on the outbox-drain / invoke-handler path, a scheduler tick or more *after* any handler-count or event-capture probe a test might await. The probe and the span are separate async paths, so "`await WaitForHandledAsync(...)` then `stopped.Single(handleSpan)`" reads the span list before it has landed. A `Task.Delay(500)` band-aid only widens the window — it still flakes under CI load. This is the bug that bit `CommandSpanTests.HandleSpan_*` and the conformance span-stitch scenarios.

Use the `SpanCapture` helper (one per test assembly): it owns the listener, lock-guards add/read against the cross-thread callback, and exposes `WaitForSpanAsync(predicate, description)` which polls the captured list for the span under assertion with a real deadline. Acquire each span you assert on through it — a span that already stopped synchronously inside the awaited `SendAsync` (the `edict.command` and `edict.event.publish` spans, which complete before delivery) returns immediately, and a deferred one is waited for. Scope the predicate by a per-test marker (the route key, a telemeterized tag, or the link back to the publish span) for the same process-global reason metrics captures are filtered, and keep span-asserting siblings in one `[Collection]`.

## Determinism in the conformance batteries (ADR-0068)

The axis-conformance batteries run over real backends but are **deterministic by construction** — they never let wall-clock timing stand in for a correctness gate. The standing rule is one sentence: **scenarios assert, the waiter layer polls.** A `*Scenarios.cs` file asserts an order-independent outcome (a committed row, an exactly-once count, a marker `ExceptionType`, a metric value, set-equality of captured events); injects faults at count-addressed points, not by wall-clock; authors its stream input; drives timers, drains, and deactivations through injected seams; and converges within a bounded number of steps. Any polling lives in a sibling `*Waiters` helper, never in the scenario.

`Task.Delay` in a `*Scenarios.cs` file is **banned, with no allowlist** — `ConformanceScenarioWallClockBan` in `Edict.Architecture.Tests` source-scans every scenario file and goes red on the first one (the `*Waiters` poll loops are exempt because the scope check is part of the matcher). When you reach for a wait, classify it against the four-class wall-clock taxonomy and use the matching seam:

- **Class A — genuine clock-gate.** You must elapse a real engine timer (a saga `[EdictSagaTimeout]` cap, an outbox backoff). Advance the injected clock: opt the fixture into `UsesVirtualClock` (it registers a `FakeTimeProvider` ahead of `AddEdict`'s `TryAdd`, so the virtual clock wins) and call `AdvanceClock(by)`. **Gotcha:** freezing the silo `TimeProvider` also freezes Orleans' stream pulling-agent timer, so stream-delivered events stop arriving — nudge the clock forward in small steps via a `*Waiters.PumpUntilAsync(AdvanceClock, condition)` helper to pump delivery, while the cap itself stays gated by an explicit `AdvanceClock`. (The real AQS and Kafka pulling agents gate on the injected `TimeProvider` too, so this generalizes to the streaming axis.)
- **Class B — grain-deactivation bridge wait.** You deactivate a grain and need to wait until Orleans has genuinely torn the activation down. This is **not** clock-drivable — teardown is not gated by the injected `TimeProvider`. Use the deactivate-and-confirm seam: `IConfirmsDeactivation` (the grain stamps an activation id on activate) + `DeactivationWaiter.DeactivateAndConfirmAsync`, which polls the activation id until it changes.
- **Class C — negative-assertion settle-wait.** "Wait, then assert nothing happened." Use the **sentinel-after** pattern: publish a later *handled* sentinel on the same serially-delivered stream / route key, wait for the sentinel to land, then assert the earlier no-effect event left no trace (the count settles at sentinel-only, the captured set excludes the suppressed id). The sentinel's arrival is the proof the earlier event has already been delivered and discarded. **Gate the wait on the sentinel's own identity, never on a count the leaked event could also satisfy** — if the bug under test is a stray extra handle, that leak drives the count to the threshold itself, so a count gate returns *before* the sentinel and the assertion passes blind to the very leak it guards. The guard cannot catch this: it is a semantically-wrong wait, not a `Task.Delay`.
- **Class D — benign poll-loop pacing.** A correct loop that drives a deterministic probe (`ForceDrainViaReminderAsync`) and gates on a count predicate, where the delay only paces retries. Correct in substance — but it must live in a `*Waiters` helper, not inline in the scenario. Extract it (the shared `ConformanceWaiters.WaitUntilAsync` is the target).

Two further fault seams keep injection count-addressed rather than wall-clock: `OutboxFaultState` / `StorageFaultState` carry count/index/kind-addressed fields (fail the Nth attempt, the Nth effect, a specific `OutboxEffectKind`) so a partial-failure batch is expressible; the `ControllableClaimCheckStore` fails the fetch a count-addressed number of times then heals, for the transient-recovery path.

This determinism is the conformance batteries' contract and is **distinct from chaos** (ADR-0066): random-per-run duplicate/reorder chaos lives in the consumer harness `Edict.Testing`, which runs over reference streaming — it never enters the conformance batteries, which are deterministic by construction.

## What not to do

- Don't test that a method was called — verify outcomes, not interactions.
- Don't use **Moq** or any mocking library for infrastructure boundaries — use real containers in the axis-conformance suites (`Edict.Azure.Streaming.Tests`, `Edict.Azure.Persistence.Tests`, `Edict.Kafka.Tests`, `Edict.Postgres.Tests`).
- Don't mock away streams/stores in the conformance suites; don't pull Testcontainers into `Edict.Core.Tests`.
- Don't share mutable state between tests.
- Don't wait on a proxy signal (a handler or event-capture count) and then assert on a span or metric emitted on a separate async path. Wait on the artifact you assert on; a `Task.Delay` standing in for that wait flakes under CI load.
- Don't assert on log output or internal exception messages unless the message is part of the public contract.
- **FluentAssertions is banned** (commercial license) — do not add it or a wrapper.
- Don't add section-divider comments inside test files. If you want to separate groups, split into separate files.
- Don't add lines when renaming identifiers in analyzer test fixtures — diagnostic assertions key on line numbers.
