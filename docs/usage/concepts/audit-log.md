# Audit log

The audit log is a regulator-grade record of *what the system decided, when, on whose authority, and with what outcome*. A consumer turns it on at wiring time; from then on every command decision and every raised event is captured to an immutable, attributable, tamper-evident store, committed atomically with the decision and queryable to reconstruct any decision chain. The reference bar is ALCOA++ (Attributable, Legible, Contemporaneous, Original, Accurate, Complete, Consistent, Enduring, Available).

The audit store is the **durable legal record**. It is distinct from traces, which are the **ephemeral, sampled overlay**: a trace may be dropped by head sampling and is gone within your retention window, whereas an audit record is written once, kept (by default) forever, and refuses to be altered. Never reach for a span when the question is "prove what happened" — that is what the audit store answers. The two are wired and read through entirely separate surfaces, and the rest of this page keeps them apart.

## Capture happens at two choke points, not the stream

Capture is sited where the decision is made, not on the event stream:

- **C1 — the command decision.** `EdictCommandHandler` records every command outcome, including a `Rejected` one. A rejected command raises no event and is invisible to any stream-based scheme, yet "why was this *denied*, and who tried?" is a regulator's first question, so the command decision is captured at the handler, the only place a rejection is visible.
- **E1 — each raised event.** One record per event, captured where the event is enqueued.

One record per fact, no double-counting. A command that raises two events yields one C1 record plus two E1 records; a rejected command yields exactly one C1 record and no E1.

Capture is **synchronous-durable**: the record stages into the same grain-state write that commits the action, so it exists before the action is acknowledged (the only way to honour *Complete* and *Contemporaneous*). The write to the backing store then happens off the hot path on an asynchronous drain, so the grain turn never pays remote-write latency. Only Command Handler grains capture in v1; sagas, projection builders, and event handlers do not.

## Attribution: the principal

Every record is attributed to a **principal** — the actor on whose authority the command was issued. The principal is an opaque, consumer-supplied string (`EdictPrincipal.Of("clerk-7")`), not a `Guid`: a B2B tenant id is a GUID, but an `oid`/`sub`, a Keycloak realm, an employee id, or a service-principal name is routinely an arbitrary slug, and Edict imposes no format validation — what a valid principal looks like is identity-provider-specific and therefore the consumer's domain.

The principal is a durable field on `EdictCommand` and `EdictEvent`, stamped once at the originating `SendAsync` from an edge resolver (`AddEdictAudit(resolver)`) and carried unchanged through the whole consequential chain. Framework-relayed sends (a saga's `Dispatch`, a schedule fire, an outbox `SendCommand`) inherit the originating principal — a recurring job stays attributed to whoever armed it — so attribution survives a crash or reactivation that ambient context never would.

Attribution **fails closed at the origin**: when auditing is on and a consumer origin send resolves no principal, the framework throws `EdictMissingPrincipalException` synchronously, before anything persists, because silently recording "nobody" is the breach auditing exists to prevent. Edict ships no `System` sentinel; a consumer doing non-user work mints its own (`EdictPrincipal.Of("system")`) and supplies it through the resolver or the explicit `SendAsync(command, principal)` overload. See the [analyzer rules](#analyzer-rules) below for the compile-time aid.

## The per-aggregate hash chain

Tamper-evidence is a **per-aggregate** hash chain, never a global one. A single global chain would need one sequencer grain serialising the whole fleet — exactly the bottleneck Orleans warns against. Instead each Command Handler grain chains its own records: a `PreviousHash` carried in grain state and a `RecordHash` computed over the *stored* record bytes, sealing each record's content and linking it to its predecessor. The first record in an aggregate's chain links to a genesis hash (32 zero bytes); each `Sequence` is monotonic within that aggregate, starting at zero.

This proves "show me this aggregate's unaltered history" — the question regulators actually ask. Global cross-aggregate order is reconstructed at *query time* from `CorrelationId` and `OccurredAt`, not from a global chain. Hashing the stored bytes rather than a fresh re-serialization makes verification immune to a serializer version bump.

`VerifyEntityChainAsync` re-walks a stored chain and returns an `EdictAuditChainVerification` (`IsIntact`, and `BrokenAtSequence` naming the first altered record). To verify a chain already held in memory — a deliberately altered copy, since a WORM store refuses an in-place edit — call the pure `EdictAuditChain.Verify(records)`.

## The query surface

Read the captured log through `IEdictAuditRepository`:

- `ByEntityAsync(entityType, entityKey)` — one aggregate's full chain, in sequence (also a time-windowed overload).
- `ByCorrelationAsync(correlationId)` — every decision one command set in motion, across grains, ordered by intent-time (`OccurredAt`). This is where the cross-aggregate chain reassembles.
- `ByPrincipalAsync(principal, from, to)` — one actor's timeline over a window.
- `VerifyEntityChainAsync(entityType, entityKey)` — the tamper verdict.
- `GetPayloadAsync(recordId)` — the captured message body as raw bytes.
- `GetMessageAsync(record)` — the same body deserialized back to the typed `EdictCommand`/`EdictEvent`.

The record (`EdictAuditRecord`) holds attribution, spine, and outcome but **not** the body: `Principal`, `CorrelationId`, `EntityType`/`EntityKey`, `MessageType`, `Kind` (`Command`/`Event`), `Outcome` (`Accepted`/`Rejected`, with `RejectionReasons` on a rejected command), `OccurredAt`, `Sequence`, the chain hashes, a `PayloadHash`, and a `PayloadReference`. The body lives in a separate `IEdictAuditPayloadStore`, keyed by record id and distinct from the claim-check store. Inlining the body into the immutable chain was rejected: it welds permanent personal data to the tamper-evident structure and could never be shredded later without breaking the chain. Keeping the chain to a hash and a reference leaves it personal-data-free and keeps the door open for the deferred crypto-shred.

## Reading a captured body

Two reads recover *what* a record decided, not merely that it decided, and the distinction between them is load-bearing:

- **`GetPayloadAsync(recordId)`** returns the captured body as the raw serialized bytes — the exact bytes the record's `PayloadHash` was computed over. This is the **integrity anchor**: it needs no live type, never goes stale, and is what you verify content against.
- **`GetMessageAsync(record)`** is the convenience over it: it deserializes those bytes back into the concrete `EdictCommand` or `EdictEvent` the consumer authored. It is boxed as `object` because a correlation drill-down (`ByCorrelationAsync`) walks records of types the caller does not know in advance — pattern-match the result against the types you own.

The typed read carries a **schema-stability obligation**. The audit log is infinite-retention, and a body is read back by deserializing it into its originating type, so turning auditing on makes every audited message type part of the permanent record's readable schema. Deleting, renaming, or breaking-changing an audited Command or Event — or its generated `[Alias]` — silently severs the typed read of every record already captured under that type. Treat audited message types as **append-only**: add fields, never remove or rename them. This is recorded discipline, not an enforced rule — an analyzer cannot know your retention intent.

When a type cannot be resolved or its bytes cannot be deserialized in the reading process — a removed, renamed, or breaking-changed type, or simply its assembly not loaded in a standalone reader — `GetMessageAsync` throws `EdictAuditMessageDeserializationException`. The bytes and hash are untouched: `GetPayloadAsync` still returns them, so the record stays forensically intact even when its typed read is gone. The byte read is the durable floor; the typed read is a legibility convenience layered over it.

## Retention and the drain

Retention defaults to **infinite** — the safest posture for *Enduring*. Edict ships no default deletion; the consumer imposes a limit per their industry's regulations.

Records drain to the store through a **dedicated audit slice**, parallel to the outbox but on its own lifecycle. Audit retention (infinite, WORM) is a fundamentally different lifecycle than business-effect delivery (drain-then-drop), and **an audit-drain failure is a compliance event, not an effect retry**. The drain runs off the command turn through a one-shot post-commit timer (the fast path), backstopped by a durable reminder armed at stage time — right after the grain-state write that makes a record durable — so undrained audit work always has a puller even if the grain deactivates before the timer runs and is never reactivated. A successful drain unregisters that reminder, so the invariant matches the outbox host: a reminder exists whenever undrained audit work exists. When the store write fails the records stay staged in grain state (durable, never dropped), the `edict.audit.drain.failure` counter increments, and the reminder retries the drain. The drain is idempotent: both stores dedup on record id, so a crash between the body write and the chain write re-drains as a no-op. The operator-facing metrics and the drain span are documented in [observability.md](../../operations/observability.md#audit-metrics-and-the-drain-span).

## Per-substrate: tamper prevention vs evidence

The hash chain is tamper-*evidence* on every substrate. Tamper-*prevention* — infrastructure that refuses the mutation outright — is substrate-specific, and the difference is honest:

- **Postgres** has **both**. The chain is an append-only table guarded by a `BEFORE UPDATE OR DELETE` trigger that raises `insufficient_privilege`. A trigger, not a bare `REVOKE`, because the table owner and a superuser bypass `REVOKE` entirely; only a trigger refuses the mutation regardless of the connecting role. The hash chain is still the cross-role evidence layer underneath it.
- **Azure** has **evidence only**, for now. The chain is an Azure Table (fan-out append rows per access path, so each query is a single-partition scan) and the body an Immutable Blob. Until the deferred blob-sealing slice, a privileged operator can rewrite a table row and detection rests entirely on the hash chain. This is a documented limitation, not a gap to work around.

## GDPR posture

A library that never touches the consumer's runtime data is neither controller nor processor; the consumer who operates the application is the controller, and legal compliance is theirs. Edict's duty is Article-25 data-protection-by-design *enablement*: minimise-to-reference by default so the chain holds no personal data, retention as an operator policy, and a future crypto-shred for the unavoidable personal slice. Per-subject **erasure** machinery is explicitly deferred to a follow-on; this design defers erasure, not capture. The **data subject** (the person the data is about) is deliberately not modelled — it is load-bearing only for erasure, and conflating it with the principal (the actor) is the most common modelling error in this space.

## Surface

- **`EdictPrincipal`** (`Edict.Contracts.Audit`) — the opaque actor identity. Mint with `EdictPrincipal.Of(value)`.
- **`IEdictSender.SendAsync(command)`** / **`SendAsync(command, principal)`** — origin send; the second overload supplies a principal explicitly for a context-free origin (worker, import, admin script, test).
- **`AddEdictAudit(resolver)`** (`IServiceCollection`) — registers the origin principal resolver and turns on origin stamping for that provider (silo or client).
- **`WithAudit()`** (`ISiloBuilder`) — arms capture on the silo and registers `IEdictAuditRepository` over the substrate stores.
- **`AddEdictAuditReader()`** (`IServiceCollection`) — registers `IEdictAuditRepository` over already-registered stores, for a non-silo reader process; on Postgres, `AddEdictPostgresAuditReader(...)` registers the stores it reads over.
- **`IEdictAuditRepository`** (`Edict.Contracts.Audit`) — the read surface above.
- **`EdictAuditRecord`**, **`EdictAuditKind`**, **`EdictAuditOutcome`**, **`EdictAuditChainVerification`** (`Edict.Contracts.Audit`) — the record and its discriminators.
- **`EdictAuditChain.Verify(records)`** (`Edict.Core.Audit`) — pure in-memory chain verification.
- **`EdictMissingPrincipalException`** — thrown at an origin send with no resolved principal when auditing is on.
- **`EdictAuditMessageDeserializationException`** (`Edict.Core.Audit`) — thrown by `GetMessageAsync` when the captured type cannot be resolved or deserialized in the reading process; the bytes stay retrievable through `GetPayloadAsync`.

## Analyzer rules

- **`EDICT023`** — an opt-in, diagnostic-only analyzer that flags a bare `IEdictSender.SendAsync(command)` so an audit-adopting consumer catches an un-attributable origin send in the IDE rather than at runtime. It is off by default (a Roslyn analyzer cannot see that `AddEdictAudit` was wired in a different assembly, so a bare send is correct on the framework's happy path) and bites at `Error` severity once enabled per project in `.editorconfig` (`dotnet_diagnostic.EDICT023.severity = error`). It exempts the explicit `SendAsync(command, principal)` overload; silence a resolver-backed site you have confirmed with `[SuppressMessage("Edict", "EDICT023")]`.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Principal`, `Audit Record`, `Audit Chain`.
- Wiring — [postgres.md](../wiring/postgres.md#reading-the-audit-log-from-a-client) (the client-side reader), [azure-persistence.md](../wiring/azure-persistence.md#the-audit-log-on-azure).
- Configuration — [postgres.md](../../configuration/postgres.md) and [azure-persistence.md](../../configuration/azure-persistence.md) — the audit table/container knobs.
- Operations — [observability.md](../../operations/observability.md#audit-metrics-and-the-drain-span) — the audit metrics and the drain span.
- Concepts — [dead-letter.md](dead-letter.md) (the forensic sibling), [telemetry.md](telemetry.md) (the trace overlay, kept distinct from this record).
- ADRs — [0063 — EdictPrincipal: attributable identity on the wire](../../adr/0063-edict-principal-attributable-identity-on-the-wire.md), [0064 — ALCOA++ audit log](../../adr/0064-alcoa-audit-log.md).
