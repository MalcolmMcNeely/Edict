---
name: edict-diagnostics
description: Use this skill when working on a consumer app built on Edict and investigating a runtime failure — a missing event, a dead-letter row, a stuck saga, a projection that never updated, or a trace that does not stitch. Points at the Dead Letter projection and the trace context first.
---

# Diagnosing a failure in an Edict consumer app

Edict's runtime gives you two cheap diagnostics before grep: the Dead Letter projection and the W3C trace context. Reach for both before reading source.

## Read the Dead Letter projection first

`IEdictDeadLetterRepository` (`Edict.Contracts.DeadLetter`) is the read-only, persistence-neutral interface every consumer can inject. It exposes two reads:

- `ListAsync(grainKey, cancellationToken)` — dead letters for a single source aggregate.
- `ListAllAsync(cancellationToken)` — the fleet-wide partition for system-wide triage.

```csharp
IReadOnlyList<EdictDeadLetterEntry> entries =
    await deadLetterRepository.ListAsync(grainKey: orderId.ToString());
```

Each `EdictDeadLetterEntry` carries the failed `Kind` (`PublishEvent` / `SendCommand` / `UpsertRow` / `InvokeHandler`), the `AttemptCount`, the `DeadLetteredAt`, the `SourceGrainKey` and `SourceGrainType`, the `EffectTarget` (encoded per kind), the captured `TraceParent`, the `ExceptionType`, the `Reason`, the `PayloadJson`, the `SourceEventId`, and a `FailureKind` discriminator (`EffectFailure` vs `BlobMissing`).

There is **no `RedriveAsync`**. Recovery is manual: re-emit the source Command or saga step for `PublishEvent` and `SendCommand`; repair the destination row by hand for `UpsertRow`, informed by the row's contents. Dead-lettering is forensic surface, not back-pressure or retry — do not treat it as a recovery mechanism.

Never read the underlying `deadletter` table directly. Always go through `IEdictDeadLetterRepository`.

## Follow the trace

A trace in Edict is **one grain's single synchronous turn**. Spans nest parent-child within a turn; every asynchronous handoff to another turn (the stream hop, a saga's command dispatch, a schedule or saga-timeout fire, a recovery drain, a dead-letter promotion) starts its *own* trace and carries one `ActivityLink` back to the span that caused it. So you do not read one giant waterfall — you read a small per-turn trace and follow its link to the cause. The chain is by trace context, not by the Command/Event Guid: the Guid is routing, trace is causality, and a saga commonly re-keys so the Guid will not stitch across domains.

The captured `TraceParent` on a dead-letter row preserves the W3C context of the failing effect's originating turn. The promotion itself emits a `edict.dead_letter.promote` span that is a new root **linking** to that context, so in the consumer's observability stack you pivot from the dead-letter promote span across its link to the trace of the turn that produced the failure.

When investigating a missing event or unrouted Command, the trace is what stitches the chain. The framework opens a single `ActivitySource` named `"Edict"`; subscribe to that. The span map: `edict.command` → `edict.command.handle` → `edict.event.publish` is one turn (the awaited API call is the one hop that stays parent-child); `edict.event.handle` and `edict.event.deduplicated` are new roots linking back to the `edict.event.publish` that raised the event; `edict.command.send`, `edict.schedule.fire`, `edict.saga.timeout`, and `edict.dead_letter.promote` each link back to their cause. Follow the link, not a nested parent, across every turn boundary.

Each dead-letter row also carries a `CorrelationId`: the chain-stable id every message in one causal chain shares, framework-stamped and propagated across the Saga hop. It is the durable grouping key that survives an unsampled trace (where `TraceParent` is null), so a fleet-wide `ListAllAsync()` grouped by `CorrelationId` shows every failure one Command set in motion. Unlike the `[EdictRouteKey]` Guid (which re-keys across domains) it stays constant end to end.

## Common failure shapes

- **`PublishEvent` dead-lettered with `FailureKind = BlobMissing`** — a claim-checked event's blob was reaped before delivery. Read `SourceEventId` on the row: the claim-check key is the event's `EventId`, so that one id both identifies the event and locates the (now-gone) parked body. The event payload itself is gone. Claim-check blobs are append-only on purpose; if you see this, something is deleting blobs out-of-band.
- **`InvokeHandler` dead-lettered** — a consumer Event Handler threw past `MaxAttempts`. Read `Reason` and `ExceptionType` on the row; the failure is in the consumer's `HandleAsync` body.
- **`SendCommand` dead-lettered** — a saga's follow-up Command exhausted attempts. The target aggregate is unavailable or its `HandleAsync` is rejecting durably. Saga progress is still readable via `GetSagaProgress` in tests; in production, query the saga grain directly.
- **`UpsertRow` dead-lettered** — a Table Projection's write to the external store kept failing. Read the row's contents to repair the destination by hand.
- **Saga dead-lettered with `ExceptionType = EdictSagaTimeoutException`** (`failure_reason = SagaTimeout`) — the saga's absolute lifetime cap fired and `OnSagaTimeoutAsync()` was not overridden, so the default routed the stall to dead-letter. The workflow started and never reached `Complete()`. The fix is on the saga: add an `OnSagaTimeoutAsync()` that compensates, or raise its `[EdictSagaTimeout]` / declare it `Unbounded` if it genuinely should run longer.
- **Saga dead-lettered with `ExceptionType = EdictSagaCompensationException`** (`failure_reason = ConsumerBug`) — the saga timeout compensation code threw: the cap fired, the consumer's `OnSagaTimeoutAsync()` override ran and threw, and the framework contained the throw — it rolled back the partial compensation, terminalised the saga to `TimedOut`, and dead-lettered with this cause rather than letting the throw poison-loop the cap reminder. The originally-thrown exception rides as the `InnerException` and its type and message are preserved in `Reason`. This reads apart from `EdictSagaTimeoutException`: that is the by-design no-override stall, this is a bug in the override's body. The fix is in the `OnSagaTimeoutAsync()` compensation code.
- **Saga dead-lettered with `ExceptionType = EdictSagaTerminalException`** (`failure_reason = SagaTerminal`) — a genuinely-new Event of a type the saga *handles* arrived at a saga that had already gone terminal (`Completed` via `Complete()`, or `TimedOut`). A redelivery of an already-handled Event still dedups silently, and an unrelated event type the saga receives off its shared stream is ignored; this row means a *new, handled* Event reached a finished saga, usually an Event the workflow did not expect to outlive `Complete()`, or a `Complete()` that should not have been called for a re-openable coordinator.
- **A Command Handler's `State` mutation did not survive** — a completing `HandleAsync` always commits its `State`, on both `Accepted` and `Rejected` and whether or not it raised an Event. So "I mutated state and it vanished" has exactly one cause now: that turn's `HandleAsync` **threw**. The throw rolls the partial mutation back to the last durable snapshot, drops the buffered Events, and — because a Command is a direct grain call, not an asynchronous Outbox effect — re-throws back to the caller of `SendAsync` rather than dead-lettering. So this leaves no dead-letter row: look at the `SendAsync` call site, which observed the exception, not the Dead Letter projection. It is no longer possible to silently lose state by mutating and raising no Event; if no exception was seen and the state is still missing, the write reached a different aggregate (check the `[EdictRouteKey]` Guid) rather than being discarded.
- **A `SendAsync` threw `EdictMissingPrincipalException`** — auditing is on (`AddEdictAudit` is wired) but the edge resolver returned no principal for this origin send, so the framework refused the send at the edge before anything dispatched or persisted: recording an action attributed to nobody is the breach auditing exists to prevent. This is **not** a dead-letter row — the throw surfaces at the `SendAsync` call site. Fix it at the origin: supply a principal explicitly with `SendAsync(command, EdictPrincipal.Of(...))` for a context-free origin (worker, import, admin script, test), or fix the resolver to yield one. To catch this at compile time instead of runtime, enable the opt-in **`EDICT023`** analyzer in `.editorconfig` (`dotnet_diagnostic.EDICT023.severity = error`): it flags every bare `IEdictSender.SendAsync(command)` so an audit-adopting consumer sees the un-attributable send in the IDE. It is off by default because an analyzer cannot see whether `AddEdictAudit` is wired in another assembly; once enabled it exempts the explicit `SendAsync(command, principal)` overload, and a resolver-backed site is silenced with `[SuppressMessage("Edict", "EDICT023")]`.
- **The `edict.audit.drain.failure` counter is incrementing** — capture succeeded (the audit record is staged durably in grain state) but the asynchronous drain to the WORM store is failing. This is a **compliance signal, not an effect retry**: the record is never dropped, a reminder retries the drain, and there is **no dead-letter row** for it — the audit slice is parallel to the outbox, not part of it, so an audit-drain failure never lands on the Dead Letter projection. Alert on any non-zero rate. Pivot from the counter's exemplar to the `edict.audit.drain` span to see the failing turn, then check the audit store's availability (the Postgres audit table, or the Azure Table + Blob the persistence provider wired). Until the drain clears, `IEdictAuditRepository` queries will not return the staged records, but they are not lost — they land once a retry succeeds.
- **A `SendAsync` or read threw `EdictMissingTenantException`** — tenancy is on (`AddEdictTenant` is wired) but the edge resolver returned no tenant for this origin send or tenant-scoped read, so the framework refused it at the edge before anything dispatched, persisted, or was read: a tenant-scoped command routed without a tenant would fall into the default key space (a silent cross-tenant leak), and a tenant-scoped read with no tenant would scope to a default partition. This is **not** a dead-letter row; the throw surfaces at the call site. Fix it at the origin: supply a tenant explicitly with `SendAsync(command, EdictTenantId.Of(...))` for a public-to-tenant establishing crossing or a context-free origin (worker, import), or fix the resolver to yield one. To catch the send case at compile time, enable the opt-in **`EDICT024`** analyzer (`dotnet_diagnostic.EDICT024.severity = error`): it flags every bare `IEdictSender.SendAsync(command)` of a tenant-scoped command.
- **A grain call threw `EdictCrossTenantAccessException`** — the runtime isolation backstop (the incoming grain-call filter) refused a call landing on a grain whose key names a tenant other than the calling turn's ambient tenant. On the common path this never fires: every key is composed from the ambient tenant, so a grain's key-tenant equals the relay tenant by construction. It surfaces only on a real divergence — a coding bug that formed a key outside the `EdictKeyComposer` chokepoint, or an illegitimate stolen-key reach into another wall — so the wall fails the call loud rather than letting a cross-tenant access through. A stolen-key reach is a direct grain call from the client, so the exception is serialized back across that hop and reaches the caller as itself (it carries an Orleans codec). This is enforcement firing correctly, not a framework fault: investigate the call site that formed the foreign key.
- **Aggregate intake is not blocked** — dead-lettering is forensic-only. A permanently failing effect does not stall its source aggregate. If a Command Handler appears stuck, the cause is not dead-lettering.
- **A Postgres grain call surfaced `EdictPostgresStorageException`** — the provider auto-retries transient `NpgsqlException`s (socket reset, timeout, pool blip) before surfacing this, so the exception means either a non-transient fault or a transient one that outlasted the retry budget. The `edict.postgres.storage.retry.count` counter (tagged `outcome=recovered\|exhausted`) shows which: an `exhausted` increment alongside the exception is the worn-out-transient case; no increment is the genuinely-non-transient case.
- **A read-your-writes read came back `CursorTimedOut`** — this is **lag, not a fault**. A read with an `EdictCursor` waits, briefly and boundedly, for the projection to catch up; if the bounded wait elapses first the read returns `CursorTimedOut` with the **latest available row**, not an exception and not a dead-letter. A read never throws for eventual-consistency lag (only caller cancellation throws). So do not hunt for a dead-letter row when a read times out: it means the projection had not yet applied the correlation within the bound. If timeouts are frequent, the projection is genuinely slow (look at outbox/drain lag and the projection's `HandleAsync` cost) or the bound is too tight (`EdictOptions.ProjectionReadTimeout`); if a *specific* correlation never lands, look for *its* dead-letter row — a poison event that dead-lettered will never reach the projection, so a wait on its cursor can only ever time out.

## A stalled saga

A saga that stalls (started and never reached `Complete()`) surfaces on two counters on the `"Edict"` Meter. `edict.saga.timeout.fired` (tagged `compensated`, `deadlettered`, or `compensation_failed` when a throwing override was contained) is the definitive "this saga hit its cap" signal; `edict.saga.completed` is its denominator, so the ratio `fired / (fired + completed)` separates a handful of timeouts from a rising failure trend. `edict.saga.progress.age` is the leading indicator: a saga whose age is climbing toward its `[EdictSagaTimeout]` will start firing caps soon. A rising `deadlettered` fired-cap rate points straight at `SagaTimeout` rows in the Dead Letter projection.

## When to look up the why

For any "why does dead-letter behave this way?" or "why no redrive?" question, invoke **`edict_lookup_adr`**. The relevant decisions:

- ADR-0015 — Outbox engine (the host, the slice, the drain).
- ADR-0018 — Dead letter (forensic-only, table-projection-backed).
- ADR-0019 — Deferred dispatch (why `SendCommand` is an Outbox effect, not an inline call).
- ADR-0020 — Claim check (and the `BlobMissing` failure kind).
- ADR-0053 — Claim-check key is the event's `EventId` (why `BlobMissing` points at `SourceEventId`).
- ADR-0060 — Trace causality at scale: a trace is one grain turn, links across turn boundaries (supersedes ADR-0003's parent-child-across-the-stream-hop model).
- ADR-0041 — Exception policy.
- ADR-0050 — Saga absolute lifetime cap (the `SagaTimeout` / `SagaTerminal` dead-letter causes).
- ADR-0063 — `EdictPrincipal` on the wire (why an origin send fails closed without a principal).
- ADR-0064 — ALCOA++ audit log (why an audit-drain failure is a compliance signal, not a dead-letter).
- ADR-0065 — Tenant as a routed identity axis (why a tenant-scoped origin send fails closed without a tenant).
- ADR-0067 — Tenant isolation enforcement and storage (the call filter behind `EdictCrossTenantAccessException`).

`edict_lookup_adr` is the load-bearing trigger for this skill: use it for any dead-letter, outbox, or trace "why" question rather than guessing.

## When the silo threw at boot

The failures above are all runtime: the silo started, then something went wrong on a turn. A silo that throws *during* host start is a different class of fault, and it is almost always a wiring-time configuration mistake. Edict validates its whole configuration once at host start through `EdictWiringValidator`, which aggregates every problem into a single `EdictWiringException` (a missing required knob, an out-of-range value, a stream provider with no persistence to pair it with). That exception is the ground-truth verdict, but it only fires once you can already start the host.

To localise the fault *before* re-running the host, invoke **`edict_check_configuration`**. It reads `Program.cs`, works out which option knobs the consumer set inside each `AddEdict*` call, and returns a best-effort verdict: required-but-unset knobs (an empty Kafka `BootstrapServers`, an unset Postgres `ConnectionString`), known footguns (an explicitly-assigned `ReplicationFactor` opting into strict mode), and incomplete extension combinations (a stream provider wired with no persistence). It resolves only set-versus-not-set and is explicitly advisory: `EdictWiringValidator`, which runs at host start with live DI, remains ground truth. Reach for it first when a silo aborts at boot, then confirm against the `EdictWiringException` message itself.

## When MCP results look off

If a Dead Letter query returns empty when you know rows exist, or `edict_list_handlers` returns nothing when handlers are obviously present, the MCP server may have indexed the wrong workspace. Invoke **`edict_describe_mcp_state`** before re-running the lookup — it reports the loaded solution path, the indexed-handler count, and the registered tool list. A mismatch between the reported solution and the consumer's actual workspace explains the surprising empty result, and the `--solution` override in `.mcp.json` is the documented fix.

## See also

- For the testing surface that surfaces dead-letter rows on `Timeline` and `GetProjectionRow`: see the `edict-testing` skill.
- For the contract attributes that stamp the trace context: see the `edict-contracts` skill.
- For the role bound to the failing grain: see the `edict-authoring` skill.
