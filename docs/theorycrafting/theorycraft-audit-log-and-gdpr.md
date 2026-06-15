# Theorycraft — regulator-grade audit log, the principal, and GDPR posture

**Status:** design-grilled (2026-06-15). The problem framing below stands; the open design questions have been resolved through a grill and the decisions are recorded in **ADR-0063** (EdictPrincipal wire shape) and **ADR-0064** (ALCOA++ audit log + GDPR posture). The "Open design questions" section near the end has been replaced with the resolved decisions. Scope was cut to *attributable, tamper-evident capture + the principal*; GDPR **erasure** machinery (per-subject encryption, crypto-shred, dead-letter/claim-check remediation, the data-subject field) is deferred to a follow-on design. This doc remains the narrative rationale; the ADRs are the decision record.

**Sibling docs:**
- [`theorycraft-multi-tenant-identity-and-surface.md`](theorycraft-multi-tenant-identity-and-surface.md) — shares the **carry mechanism** this doc needs. The audit log's `principal` enters the system exactly the way that doc's tenant id does: an authenticated claim resolved at the edge, stamped onto the envelope, never trusted from a command body. If both features ship, they should share one resolver seam and one envelope-capture point, not two.
- [`theorycraft-tenant-scoped-substrate.md`](theorycraft-tenant-scoped-substrate.md) — shares the **dead-letter-as-cross-cutting-store** problem. The dead-letter row is the closest thing Edict already has to an audit record (`PayloadJson` plus source metadata), and it is also the sharpest GDPR liability (materialised personal data pooled fleet-wide). Both docs touch it; reconcile in the ADR.
- [`dead-letter.md`](../usage/concepts/dead-letter.md) (ADR-0018, ADR-0041) — the forensic row is the prototype. This doc generalises "forensic on failure" to "forensic on every decision."
- [`idempotency.md`](../usage/concepts/idempotency.md) (ADR-0002), [`events.md`](../usage/concepts/events.md) (ADR-0015) — the outbox and the assign-once `EventId` are the load-bearing primitives the capture reuses.

## The problem

A consumer in a heavily regulated industry (financial, medical) is required to keep a durable, tamper-evident record of *what the system decided, when, on whose authority, and why*. The reference bar is **ALCOA++**: Attributable, Legible, Contemporaneous, Original, Accurate, plus Complete, Consistent, Enduring, Available. Regulators audit the record, and a record that can be silently altered or quietly dropped is worse than none.

Edict has no answer for this today, and the gap is structural. Three findings frame it:

1. **Orleans gives nothing usable off the shelf.** `JournaledGrain` + the log-consistency providers persist *state*, not a queryable audit of record: `StateStorage` discards the event log entirely, `LogStorage` keeps it but rewrites the whole sequence on every access (Microsoft's own docs call it unfit for production), and `RetrieveConfirmedEvents` is per-grain and version-ranged only, so it cannot trace a decision across grains. The AQS stream provider is **not rewindable**, so the stream is a transport, not a record. Whatever we build, we build.

2. **Edict has already built ~90% of it, twice.** The dead-letter forensic row (ADR-0018/0041) already captures who (`SourceGrainKey`/`SourceGrainType`), what (`Kind`, `PayloadJson`, `SourceEventId`/`SourceEventType`), when (`DeadLetteredAt`), why (`ExceptionType`, `Reason`, classified `FailureKind`), and the causal spine (`CorrelationId`, `TraceParent`). It is append-only by contract (no `DeleteAsync` anywhere; `IEdictClaimCheckStore` documents the rule explicitly), drained through the outbox, projected fleet-wide. It only fires on *failure*. And the outbox itself (ADR-0015) is exactly the seam an audit log wants: a record committed atomically in the same one grain-state write as the action, then forwarded asynchronously off the hot path.

3. **The one thing Edict is missing is *who*.** `EdictCommand` carries `CommandId` and `CorrelationId` and nothing else. There is no principal, no actor, no caller identity anywhere on the command or on `IEdictSender`. ALCOA's first letter is Attributable, and Edict cannot satisfy it without a wire-shape change.

So the design is: generalise dead-letter from "forensic on failure" to "forensic on every decision," ride the outbox to capture it, add a principal to the command, and drain to a single WORM-backed, correlation-indexed query store. The hard parts are tamper-evidence under Orleans' distribution model, and GDPR.

## The decision this primitive does not make for you

**Authentication of the principal is the consumer's domain and stays there**, identically to the tenant story in the sibling docs. The audit record's "who" is an *authenticated* claim deposited on the request principal at the Web/API edge. Edict cannot invent it and must never trust a principal value carried in a command payload (confused-deputy risk: a caller could otherwise forge any actor into the permanent record). What Edict owns is faithful **capture, propagation, and durable tamper-evident storage** of an already-authenticated identity, plus the decision and its outcome.

**Legal GDPR responsibility is also not Edict's** (Section "GDPR posture" makes the case in full): the consumer who operates the application is the *controller*; a NuGet library that never touches their runtime data is neither controller nor processor. Edict's duty is Article-25-style *enablement* (data protection by design): give the controller primitives that make compliance possible, never force them into a corner where it is not.

## What the primitive does

An **audit record** is an immutable, attributable statement that a decision happened, committed atomically with the decision and drained to a tamper-evident store the consumer can query to reconstruct any decision chain.

Three parts, mirroring the outbox shape that already works:

**Capture side.** At each command and event choke point (next section), the framework stages an audit record into the same grain-state write that commits the action. It is durable before the action is acknowledged, which is the only way to satisfy ALCOA *Complete* + *Contemporaneous*. The per-turn cost is one in-memory slice entry plus a local hash, not a network round trip.

**Carry side.** The principal rides the envelope, captured once at the edge from the authenticated claim, the same mechanism the identity sibling doc designs for tenant. The `OccurredAt` intent-time stamp (ADR-0026) and the assign-once `EventId` (ADR-0051) are already the right shape for an audit timestamp and an immutable record id; reuse them rather than minting parallel ones.

**Honour side.** The records drain asynchronously through the existing executor machinery to a WORM-backed store (a new dumb persistence seam, `IEdictAuditStore`, shaped like `IEdictTableStore`), and dead-letter on drain exhaustion using the backstop that already exists. The store is a single logical append-only log, physically partitioned, indexed by `CorrelationId`.

```csharp
// Edge (consumer code): principal comes from the authenticated claim, same seam as tenant.
services.AddEdictAudit(httpContext => httpContext.User.FindFirst("oid")?.Value);

// Everything downstream is captured automatically. A regulator question
// ("why was order X rejected, and who tried?") becomes one query:
IReadOnlyList<EdictAuditRecord> chain = await auditRepository.ByCorrelationAsync(correlationId);
// ^ the full decision chain across every grain it touched, ordered by OccurredAt.
```

The win is a complete, tamper-evident record at near-zero hot-path cost. The cost is write amplification (every command adds a slice entry) and the GDPR surface that a permanent personal-data store creates.

## Where to capture — the choke points already exist

Every command and every event funnel through a handful of single points. Two are load-bearing, and one matters precisely because it sees what the others miss:

- **C1 — `EdictCommandHandler.ValidateAndHandleAsync`** (`Edict.Core/Commands/EdictCommandHandler.cs:393`). The full command spine. It is the **only** place that sees a *rejection*: an `EdictCommandResult.Rejected` with its `EdictRejectionReason[]` raises no event and would be invisible to any stream-based scheme. A regulator's first question is usually "why was this *denied*, and who attempted it?" Only C1 can answer it. This is the decisive reason the capture is not a stream subscriber.
- **E1 — `OutboxHost.EnqueueRaisedEventsAndDrainAsync`** (`Edict.Core/Outbox/OutboxHost.cs:262`). The single point where `EventId` is minted (once, ADR-0051) and `CorrelationId` inherited. Every event is born here exactly once, so capturing here inherits the immutable-id and intent-time guarantees for free.
- **DL1 — `DeadLetterPromoter.Promote`** is the existing failure capture and stays as-is; an audit record and a dead-letter row are now siblings, not rivals.

Capturing at C1 + E1 (not at the stream) yields commands *and* events, accepts *and* rejects, with no extra identity plumbing.

**Reject the obvious shortcut: an audit List-projection subscribed to the event stream.** It is cheap and reuses ADR-0061 wholesale, but it captures only events, never commands, and never a single rejection. It records the consequences of decisions, never the decisions. Fine as a *secondary* read model over the audit store; wrong as the capture.

## One log, or many

**Both, at different layers**, which is where every source converged:

- **Capture is per-grain (many).** Each aggregate stages its own audit records in its own state write. Writes stay distributed and scale. Routing every audit write through one coordinator grain would be the "bottleneck grain" Orleans explicitly warns against and would serialise the whole fleet behind one actor.
- **Query is one logical store.** The per-grain captures drain into a single append-only, WORM-backed log, **partitioned physically by time** (natural for retention rules) and optionally by tenant, **indexed by `CorrelationId` + grain key + principal.**

`CorrelationId` is the cross-grain spine, already threaded through every command and event. "Trace this decision across every grain it touched" is a select-by-`CorrelationId` ordered by `OccurredAt`. The **trace links** (ADR-0060) overlay the observability view. Keep the two strictly distinct: **the audit store is the durable legal record; traces are the ephemeral, sampled debugging overlay.** Compliance must never depend on sampled telemetry.

## Tamper-evidence and ordering — the hard Orleans-specific part

ALCOA wants tamper-*evidence*, not merely append-only. The textbook tool is hash chaining (`this_hash = SHA256(canonical_payload ‖ prev_hash)`). Orleans makes the *global* version an anti-pattern:

- **Global hash chain: reject.** One chain needs one sequencer grain, which is a bottleneck grain that serialises every write. No.
- **Per-aggregate hash chain: adopt.** Each grain chains its own records (`prev_hash` lives in its state, the hash is a cheap local step on the turn it is already taking). Distributed, scales, and proves an aggregate's history was not altered, which is the question regulators actually ask ("show me this account's unaltered history"). It loses *global* order, but `CorrelationId` + intent-time `OccurredAt` reconstruct cross-grain order at query time.
- **Global tamper-evidence, if needed: periodic Merkle anchoring, off the hot path.** A low-frequency background pass (a singleton grain or external job) reads sealed segments and anchors a Merkle root somewhere trusted. Cross-stream tamper-evidence with zero hot-path contention. Defer to a later slice.
- **WORM is infrastructure, via the substrate seam (ADR-0042/0030).** Azure Immutable Blob / append-blob with a time-based retention policy on the Azure persistence package; an append-only Postgres table with `REVOKE UPDATE/DELETE` plus the hash chain on the Postgres package. Application-layer append-only is the floor; infra immutability is the proof. Retention stays an operator lifecycle policy, exactly as claim-check and dead-letter already are.

## Performance posture (non-negotiable)

The split is the whole game: **synchronous durable capture inside the grain-state write** (the only way to honour *Complete*: the record exists before the action is acknowledged), **asynchronous drain to the WORM store** (so the grain turn never pays remote-write latency). This is the outbox Edict already runs, so the marginal per-turn cost is one slice entry and one hash, not a round trip. Batch the drain. A dedicated `AuditSlice` parallel to `OutboxSlice` is worth considering if early load shows audit drain contending with business-effect drain; it also cleanly separates audit retention policy from effect delivery. Start with the shared outbox; split if measured.

## What "principal" means

"Principal" is the **actor on whose authenticated authority a command was issued**: a human user, a service identity, a scheduled job's owner. It is ALCOA's *Attributable* and it is the field Edict lacks today. Precision matters here because two GDPR roles hide behind one word:

- The **principal / actor** is *who performed the action*. It answers Attributable. In GDPR terms the principal is usually a *data subject in their own right* (an employee id, an operator's `oid`), so the principal field is itself personal data even when the command payload is not.
- The **data subject** (GDPR Article 4(1)) is *the person the data is about*. In a command like `CloseAccount`, the actor (an employee) and the data subject (the customer) are different people, and the audit record may contain personal data about **both**. Conflating them is the most common modelling error; the design must keep them as distinct fields.

**Reuse the identity sibling's resolver seam, do not invent a second one.** The principal enters exactly as the tenant does: an `AddEdictAudit(resolver)` edge registration reads the authenticated claim (`oid`, `sub`, or a custom claim), the framework stamps it onto the envelope at the originating `SendAsync`, and a grain-entry relay re-stamps it onto sends raised within the turn (saga `Dispatch`, schedule fire, outbox `SendCommand`) so a chain that began under principal P stays attributable to P through every async hop. The non-negotiables are identical: read from the authenticated principal, never the command body; fail closed when auditing is on and no principal resolves; `Edict.Contracts` stays free of any IdP SDK and any `ClaimsPrincipal` (the seam is a `Func`, an ASP.NET adapter is a thin optional helper).

**Wire shape.** This is a deliberate `EdictCommand` change and supersedes the relevant clause of ADR-0006/0046: an `EdictPrincipal` (opaque, length-bounded, charset-validated string at the contract, for the same cross-IdP reasons the sibling lands on a string tenant id) added to the command/event envelope, MessagePack-friendly, no `[Union]`. The tenant-id type disagreement between the two sibling docs applies verbatim to the principal type; reconcile all three in one ADR.

## GDPR posture — the immutability vs erasure tension

> Research synthesis, not legal advice. Article numbers refer to Regulation (EU) 2016/679; UK GDPR mirrors them and ICO guidance is cited interchangeably. Where a point is legally unsettled it is flagged, not asserted.

An immutable, WORM, hash-chained audit log is the architectural opposite of erasable. If it contains personal data, **Article 17 (right to erasure)** appears to demand exactly the operation the design forbids: deleting or mutating a record breaks the chain and destroys the tamper-evidence that is the log's reason to exist. That tension is real. It resolves into three moves, in priority order.

**1. Minimise so the chain holds references and integrity proofs, not personal payloads (Article 5(1)(c), best move).** If the audit purpose (prove who did what, when, in what order, with what outcome) is met by an opaque principal id, an `EventId`, a `CorrelationId`, the decision/outcome, and a hash of the payload, then storing the *plaintext payload* is unnecessary and violates data minimisation. A log that contains no personal data has nothing to erase. This is why the default audit record should reference and hash, not copy, the command/event body. The existing dead-letter `PayloadJson` (materialised, human-readable) is the cautionary counter-example: it is the one Edict surface that already bakes personal data into a permanent store, and the audit design must not repeat it by default.

**2. Where personal data must be in-chain, make it individually crypto-shreddable (defensible, legally unsettled, flag it).** Encrypt the personal slice under a per-data-subject key held in a separate mutable key store; erasure destroys the key. The ciphertext and every hash stay byte-identical, so the chain and tamper-evidence survive, but the plaintext becomes computationally unrecoverable, and shredding subject A does not touch subject B. Honest caveats the design must carry:
   - **No binding authority confirms crypto-shredding as "true erasure."** The frequently-cited "EDPB Guidelines 5/2019 bless it" claim is a misattribution (those guidelines are about search-engine delisting). The strongest real anchors are ICO's "put beyond use" doctrine and Recital 26's identifiability test, both interpretive, not dispositive.
   - **Surviving ciphertext is most safely treated as pseudonymised, i.e. still personal data (Recital 26, Article 4(5)),** not anonymised. Pseudonymised data stays fully in GDPR scope; only genuinely irreversible anonymisation leaves scope, and the bar is high and contextual. Designers routinely believe they anonymised when they only pseudonymised; treat that as the default error.
   - **Erasure is only as real as key destruction is irreversible and auditable.** If keys are backed up, escrowed, or sit in a KMS with a soft-delete recovery window, the data is recoverable and erasure failed. Backup hygiene shifts from "delete the record" to "ensure no key copies survive." "Computationally unrecoverable" is also time-bound (algorithm breaks, quantum, harvest-now-decrypt-later).
   - The act of crypto-shredding should itself be a logged, tamper-evident audit event: it is evidence that an erasure request was executed.

**3. For the retention-mandated slice, lean on the Article 17(3) carve-outs and a clear Article 6 basis.** Article 17 is not absolute. 17(3)(b) disapplies erasure where processing is necessary for compliance with a legal obligation under Union/Member-State law; 17(3)(e) where necessary for the establishment, exercise, or defence of legal claims. Sector retention law (MiFID II commonly 5 years, FINRA/SEC 17a-4 commonly 6 years on WORM media that is itself *legally mandated*, SOX 7 years, medical often 10+) is exactly such an obligation. For that slice, for that period, Article 17 simply does not bite, and the log doubles as **Article 5(2) / 30 accountability evidence** rather than pure liability. Two honest qualifiers: the exemption is purpose-bound and proportionate (it does not justify keeping *everything* forever), and US statutes are not GDPR's "Union or Member-State law," so they support 17(3)(e) and apply where the controller is US-regulated, but do not by themselves satisfy 17(3)(b) for an EU data subject. The lawful basis for the audit log itself (typically 6(1)(c) legal obligation, dovetailing with 17(3)(b), or 6(1)(f) legitimate interest in security/fraud prevention per Recital 49) must be identified up front; it is the most-skipped step.

The residue that none of the three fully covers, personal data captured "for completeness" with no retention shield and no minimisation, is where the work concentrates. The design principle: **minimise to references by default, crypto-shred the unavoidable personal slice, retention-shield the legally-mandated slice, and make every erasure itself an audited event.**

## What "right to delete" means in Edict, concretely

Today, **Edict exposes no deletion path at all.** There is no `ClearStateAsync`/`DeleteAsync` on any consumer seam; claim-check and dead-letter are append-only by explicit design; grain state lives until Orleans deactivates the grain and then persists last-write-wins; projection rows have no delete seam; retention is purely operator-side (Azure Blob lifecycle, Postgres equivalent). So "right to delete" is **currently entirely the consumer's problem, and Edict gives them almost no tools to solve it.** That is the weakness to fix, not a posture to defend.

Personal data physically comes to rest in Edict in six places, and an erasure story has to name each:

| Surface | Type | Form | Erasure today |
|---|---|---|---|
| Grain state | `GrainEnvelope<TPayload>.Payload` | opaque serialized bytes | none (Orleans last-write-wins) |
| Outbox entry | `OutboxEntry.Payload` | opaque bytes | none (drained then dropped) |
| Claim-check | `IEdictClaimCheckStore` value | opaque bytes, keyed by `EventId` | none, append-only by contract |
| List projection rows | consumer POCO | bytes or columns | no delete seam |
| Dead-letter | `EdictDeadLetterEntry.PayloadJson` | **materialised JSON** | none, read-only, fleet-pooled |
| Event envelope on-stream | `EdictEventEnvelope.InlinePayload` | opaque bytes or claim-check pointer | transient (AQS not durable) |

Under the recommended design, "right to delete" becomes a defined operation: **crypto-shred the per-subject key**, which renders the personal slice in every one of those at-rest surfaces (audit store, claim-check, dead-letter payload, projection rows that opted into envelope encryption) computationally unreadable in one act, while leaving the immutable structure and the legally-retained non-personal fields intact. That is only achievable if the personal slice was encrypted under a per-subject key at write time, which is itself a framework primitive to build, not a consumer afterthought. Dead-letter `PayloadJson` and claim-check are the two surfaces that most urgently need to move from "plaintext, append-only, no erasure" to "encrypted-at-write, crypto-shreddable," because they are the ones that already bake personal data into permanence today.

## Strengths and weaknesses of Edict's GDPR posture

**Whose job is it?** The consumer who operates the app is the **controller** (Article 4(7)): they determine the purposes and essential means (what data, how long, who sees it). A library that ships as a NuGet package and never touches their runtime data is **neither controller nor processor**: EDPB Guidelines 07/2020 give the on-point example that even a software provider who incidentally glimpses personal data while fixing a bug is not a processor. Edict would only become a *processor* if it operated a service that processed consumer personal data on their behalf (a hosted audit backend, telemetry that ships personal data to the maintainer, managed key custody), none of which a NuGet package does. **So GDPR legal compliance is the consumer's, and the framework's duty is Article-25 enablement.** This is the accurate and defensible answer, not a dodge.

**Strengths (what Edict is well-positioned for):**
- **The outbox gives atomic, contemporaneous capture.** Edict can offer accountability evidence (Articles 5(2), 30, 32, 33) that most frameworks cannot, with a clean integrity guarantee, because the record commits in the same write as the action.
- **Strong existing identity spine.** `CorrelationId` and assign-once `EventId` make a minimised, reference-and-hash audit record natural; the chain can be personal-data-free by default.
- **The substrate seam (ADR-0030/0042) is the right lever for WORM and crypto-shredding** without `Edict.Core` learning any storage SDK, and retention is already modelled as an operator policy.
- **Envelope-carry + edge-resolver already designed** (the tenant sibling) means principal capture is a known, airtight pattern, not a research project.

**Weaknesses (what is missing or actively risky today):**
- **No principal at all.** ALCOA Attributable is unsatisfiable without a wire-shape change; this is the headline gap.
- **No erasure tooling whatsoever.** Every at-rest surface is delete-free today, and two of them (dead-letter `PayloadJson`, claim-check) store personal data in a permanent, fleet-pooled form with no encryption and no shred path. As shipped, a consumer who must honour an Article 17 request has no framework-supported way to do it short of destroying the whole store.
- **No per-subject encryption primitive,** so crypto-shredding (the only reconciliation that preserves the immutable log) is not currently expressible.
- **Dead-letter pools every tenant's materialised payload into one partition** (the sibling tenant doc's sharpest finding), which is simultaneously a tenant-isolation breach and a GDPR data-minimisation problem.
- **Crypto-shredding's legal status is unsettled,** so even the recommended design carries residual risk that must be documented to consumers rather than sold as guaranteed erasure.

**Net posture:** Edict is *structurally* well-placed to be a best-in-class enabler of regulated, GDPR-aware auditing, and is *currently* close to the worst case on erasure specifically (permanent, plaintext, un-shreddable personal data on at least two surfaces). The gap between the two is the work this doc scopes.

## Resolved design decisions (grill of 2026-06-15)

The nine open questions were resolved as follows. ADR-0063 and ADR-0064 are the decision record; this list is the map from the original question to the verdict.

1. **Audit record shape.** **Standalone `EdictAuditRecord`**, *not* unified with the dead-letter row. The proposed `Accepted`/`Rejected`/`Failed` discriminator conflated two different lifecycle moments (decision outcome at capture vs delivery failure later on drain exhaustion) and would have pulled the shipped dead-letter surface into the blast radius. `Command`/`Event` kind; `Accepted`/`Rejected` + `EdictRejectionReason[]` on commands. Possible later shared core is a recorded follow-on, not v1.
2. **Principal type.** **Opaque consumer-supplied `string`, no `Guid` fast-path, and Edict imposes no format validation** (IdP-specific, the consumer's domain). Edict's providers handle arbitrary strings safely; the Sample app demonstrates consumer-side validation. This casts the sibling docs' deferred vote for the principal: a string principal and a (possibly `Guid`) tenant id can legitimately differ, reconciled in the future tenant ADR.
3. **Default capture content.** **Option 2: reference-and-hash in the chain, body captured to a separate referenced store** (`IEdictAuditPayloadStore`). Capture is *not* deferred — ALCOA needs the content — only *erasure* is deferred. Inlining the body into the chain was rejected (un-shreddable, the dead-letter `PayloadJson` mistake). v1 bodies are plaintext at the application layer, encrypted-at-rest by infrastructure.
4. **Per-subject encryption and key custody.** **Deferred** to the GDPR follow-on, together with crypto-shred. The chain/payload separation (decision 3) is what lets it land later without a chain rewrite.
5. **Shared vs dedicated slice.** **Dedicated `AuditSlice` from the start** (diverged from the original "start shared"). The retention divergence (infinite/WORM vs drain-then-drop) and the compliance-event nature of an audit-drain failure are already real, not hypothetical.
6. **Per-aggregate hash chain mechanics.** **Per-aggregate (per-command-handler) chain**, `prev_hash` in grain state, `this_hash` over the **stored record bytes** (immune to a MessagePack version bump), Merkle anchoring **deferred**.
7. **Retention.** **Infinite default**, the consumer imposes a limit per their industry's regulations. Edict ships no default deletion. Per-message-type retention *classes* fold into the deferred erasure work.
8. **Dead-letter and claim-check remediation.** **Deferred** to the GDPR follow-on, to be sequenced against the tenant sibling's dead-letter-partition change so the row is fixed once for both concerns.
9. **Querying surface.** **`IEdictAuditRepository`** (`ByCorrelationAsync`/`ByPrincipalAsync`/`ByEntityAsync`, time-ranged) + `GetPayloadAsync` + a first-class `VerifyEntityChainAsync`. Azure Table implements the three access paths via fan-out append rows (cheap because append-only, and Postgres is the throughput substrate); Postgres uses secondary indexes.

## Constraints from existing decisions

- **ADR-0015 (outbox).** The capture mechanism. Audit records stage in the same atomic write; reuse the executor and the dead-letter backstop rather than inventing a parallel engine.
- **ADR-0018 / 0041 (dead-letter, exception philosophy).** The forensic row is the prototype and the dead-letter store is the closest existing audit primitive. A missing-principal-when-auditing-is-on condition is a wiring/representability fault: a typed `Edict*` throw at the boundary that fails closed, never a silent capture of a null actor.
- **ADR-0026 (OccurredAt at Raise) / ADR-0051 (EventId assign-once).** Already the correct audit timestamp and immutable record id. Do not mint parallel ones.
- **ADR-0006 / 0007 / 0046 (wire shape, contracts boundary, canonical authoring).** Adding `EdictPrincipal` to the envelope is a deliberate supersession of the message shape; it crosses the wire and needs a stable MessagePack-friendly concrete type, no `[Union]`, and `Edict.Contracts` must stay free of any IdP SDK.
- **ADR-0030 / 0042 (substrate seam, persistence split).** The WORM audit store and the per-subject encryption / key-store hook are new dumb persistence seams reached through the substrate boundary, provider-implemented (Azure Immutable Blob, Postgres append-only + revoked UPDATE/DELETE).
- **ADR-0060 (trace causality).** Traces overlay the audit store for debugging; they are not the record. Do not let compliance depend on sampled spans.
- **ADR-0061 (projection species).** A queryable audit read model can be a List projection over the audit store, but the *capture* is not a stream subscriber (it would miss commands and rejections).
- **CONTEXT.md (brand prefix).** All consumer-typed surfaces (`EdictPrincipal`, `EdictAuditRecord`, `AddEdictAudit`, `IEdictAuditRepository`) are `Edict`-prefixed per ADR-0017.

## Where this lands in the code

Rough sketch, verify against current structure before designing:

- `Edict.Contracts` — `EdictPrincipal` wire type; the principal field on the command/event envelope; `EdictAuditRecord`; read-only `IEdictAuditRepository`; the `AddEdictAudit(resolver)` surface and the `IEdictAuditStore` / per-subject-encryption seams (shaped like the existing dumb store seams).
- `Edict.Core` — capture at C1 (`EdictCommandHandler.ValidateAndHandleAsync`) and E1 (`OutboxHost.EnqueueRaisedEventsAndDrainAsync`); the per-aggregate hash chain in `GrainEnvelope`; the grain-entry principal relay (side by side with the trace-context capture and the tenant relay, if both ship).
- `Edict.Azure.Persistence` — `IEdictAuditStore` over Azure Immutable Blob / append blob with time-based retention; remediate claim-check and dead-letter to encrypt-at-write.
- `Edict.Postgres` — `IEdictAuditStore` over an append-only table with revoked UPDATE/DELETE and the hash chain; same remediation.
- `Edict.Generators` / `Edict.Analyzers` — principal-aware envelope emission; an `EDICT0xx` analyzer requiring a resolved principal on origin sends when auditing is on (and *not* flagging framework-relayed internal sends). Top test target: silent-failure risk.
- `Edict.Tests.Conformance` — an audit-completeness battery (every command and every event, accept and reject, produces exactly one immutable record; the chain verifies; crypto-shred renders the personal slice unreadable while the chain stays intact), per substrate.
- `Sample.*` — a regulated-industry variant showing a decision traced end to end by `CorrelationId`, and an Article-17 request honoured by crypto-shred.
- CONTEXT.md — glossary entries for **audit record**, **principal**, **crypto-shred**.
- New ADR(s) — the principal wire-shape change (superseding the message-shape clause of ADR-0006/0046), the capture-at-choke-point decision, the per-aggregate-chain + deferred-Merkle decision, the WORM store seam, the per-subject encryption / crypto-shred erasure model, and the GDPR controller/processor stance.

## Suggested first slice

Smallest thing that proves the design, and it should prove *attributable capture and tamper-evidence*, not ergonomics:

1. `EdictPrincipal` in `Edict.Contracts` and the principal field on the command/event envelope; `AddEdictAudit(resolver)` stamps it at the originating `SendAsync`.
2. Capture at C1 only (commands, including rejections): one `EdictAuditRecord` staged in the command's atomic write, with a per-aggregate `prev_hash`/`this_hash` chain.
3. One substrate (Postgres, cleanest append-only table), drain the record, assert it is present, immutable, and chain-valid.
4. One conformance test: issue an accepted command and a rejected command under a known principal, assert two records with correct principal, outcome, and an unbroken hash chain; attempt an UPDATE and assert the store refuses it.
5. Fail-closed: auditing on + null principal throws a typed `Edict*` at the edge.

That validates attributable capture, atomic durability, and tamper-evidence against a real substrate on the highest-value surface (commands, where rejections live). Event capture (E1), crypto-shred erasure, the WORM blob store, dead-letter/claim-check remediation, the analyzer, and the query read model each follow as their own slices once the capture mechanism and the chain exist.

## Sources

GDPR research was conducted against current (2026) primary statutory text and regulatory guidance; Orleans and audit-pattern research against current Microsoft docs and the event-sourcing literature. Key references:

- GDPR primary text — Art. 4 (definitions) https://gdpr-info.eu/art-4-gdpr/ , Art. 5 (principles) https://gdpr-info.eu/art-5-gdpr/ , Art. 6 (lawful basis) https://gdpr-info.eu/art-6-gdpr/ , Art. 17 (erasure + 17(3) exemptions) https://gdpr-info.eu/art-17-gdpr/ , Art. 30 (records) https://gdpr-info.eu/art-30-gdpr/ , Recital 26 (identifiability) https://gdpr-info.eu/recitals/no-26/
- ICO right-to-erasure ("put beyond use", exemptions) — https://ico.org.uk/for-organisations/uk-gdpr-guidance-and-resources/individual-rights/individual-rights/right-to-erasure/
- EDPB Guidelines 07/2020 (controller/processor; software-provider example) — https://www.edpb.europa.eu/our-work-tools/our-documents/guidelines/guidelines-072020-concepts-controller-and-processor-gdpr_en
- EDPB Guidelines 01/2025 (pseudonymisation, still-personal-data) — https://www.edpb.europa.eu/system/files/2025-01/edpb_guidelines_202501_pseudonymisation_en.pdf
- Crypto-shredding mechanics and skepticism — https://www.seald.io/blog/data-destruction-using-crypto-shredding , https://secupi.com/crypto-shredding-is-not-nirvana-for-right-of-erasure-or-rtbf-compliance/ , and the IAPP "encrypted data may still be personal" op-ed https://iapp.org/news/a/op-ed-encrypted-data-may-still-be-personal-under-gdpr
- Orleans event sourcing and its limits — log-consistency providers https://learn.microsoft.com/en-us/dotnet/orleans/grains/event-sourcing/log-consistency-providers , JournaledGrain https://learn.microsoft.com/en-us/dotnet/orleans/grains/event-sourcing/journaledgrain-basics , streams (rewindability) https://learn.microsoft.com/en-us/dotnet/orleans/streaming/streams-programming-apis , best practices (no blocking, bottleneck grains) https://learn.microsoft.com/en-us/dotnet/orleans/resources/best-practices
- Sector retention (WORM mandate context) — SEC Rule 17a-4 / FINRA 4511 and MiFID II Art. 16/25 record-keeping; Azure immutable blob storage https://learn.microsoft.com/en-us/azure/storage/blobs/immutable-storage-overview
- One caution carried forward: the popular claim that "EDPB Guidelines 5/2019 bless crypto-shredding" is a misattribution (those guidelines concern search-engine delisting). Crypto-shredding is defensible best practice with residual, unsettled legal risk, not regulator-confirmed erasure. Present it that way to consumers.
