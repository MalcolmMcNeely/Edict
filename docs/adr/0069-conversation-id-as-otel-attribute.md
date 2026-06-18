# Edict's correlation strategy in OpenTelemetry: a queryable conversation id across three scopes

Status: Accepted

This builds on [ADR-0060](0060-trace-causality-at-scale-one-turn-links.md). That ADR settled the *trace* shape — a trace is one grain turn, an `ActivityLink` connects every cross-turn handoff — and it deliberately rejected listening to the substrate's own OTel to stitch causality. It left one question unanswered, and a consumer asks it the first time they open a tracing UI: *how do I see the whole picture across the split traces?* The honest pre-ADR answer was "follow links by hand, trace to trace" — there was no single query that returned a whole conversation, because the chain-stable id that survives every hop, `Correlation Id`, lived only on the wire and in the audit store, never on a span. This ADR closes that gap and, in doing so, settles how Edict expresses business correlation in OTel.

## The decision

Edict exposes correlation in OTel at **three distinct scopes**, each answering a different debugging question, never collapsed into one flat identifier:

| Scope | Question it answers | Carrier |
|---|---|---|
| Turn | "what happened in this one grain turn?" | `trace_id` (ADR-0060) |
| Conversation | "what is the whole Command → Event → Command chain this turn belongs to?" | the span attribute **`messaging.message.conversation_id`** |
| Business operation | "what did this saga / this recurring schedule do across all the conversations it spawned?" | the **grain key** already tagged on the turn span (`edict.grain.*`) |

The new, load-bearing addition is the middle row: the conversation id becomes a **queryable span attribute** so an operator filters `messaging.message.conversation_id = <id>` in Jaeger/Tempo/the Aspire dashboard and gets every turn in that conversation in one query, then follows the existing links to pivot *between* conversations. The third scope was already present — a saga or schedule grain key threads its whole multi-conversation lifetime — and is the reason we do **not** smear an originating conversation id across the turns it spawns: that would corrupt the conversation scope to manufacture a business-operation scope that already exists natively.

## Per-turn, not per-origin

Each turn span carries the conversation id of **its own work**, never an ancestor's. This matters most at the two timer-triggered turns, which are by deliberate design *fresh causal roots* (a schedule fire mints a new conversation; a saga cap fire stages a command that mints one lazily). Their spans therefore carry the *fresh* conversation id, and the trace link — already built from the arming turn's persisted context — is what ties them back to whoever armed them. The rule reads cleanly against ADR-0060: **the conversation-id attribute answers "which conversation is this turn's work part of"; the trace link answers "what caused this turn."** A correlation query returns one conversation; crossing a deliberate conversation boundary (the arming turn → the fire) is a link traversal, which is exactly what links are for.

## The standard name, and why we renamed

The attribute is named after the OpenTelemetry messaging semantic convention `messaging.message.conversation_id` — defined by the spec as "the conversation ID … sometimes called 'Correlation ID'." Off-the-shelf tooling already understands the concept under that key, so a bespoke `edict.correlation_id` would have forfeited interop for no gain. There is one tag, the standard one; no house-namespaced alias.

Because OTel's canonical noun for this concept is *conversation* (with *correlation* as the spec's own colloquial alias), and Edict has no released consumers to hold compatible, we take the ambitious-but-correct step of renaming the house term to match: **`Correlation Id` becomes `Conversation Id`** across every living surface — the public `EdictCommand` / `EdictEvent` / `EdictAuditRecord` / `EdictDeadLetterEntry` members, the audit repository query, `EdictCursor`, the glossary, the usage docs, the skill bodies, and code comments. This is a breaking public-surface rename (`feat!:` with a `BREAKING CHANGE:` footer; the version bump stays the manual release-time choice of [ADR-0059](0059-release-automation.md)). The decision logs 0001–0068 are frozen records and keep their original "Correlation Id" prose in their original context; this ADR carries the terminology forward.

## How the attribute reaches every turn — including the safety net

Edict is a library, not an application, so it cannot rely on the consumer having wired a baggage span processor into their OTel pipeline: the *queryable* attribute must be set by Edict itself, directly on the span. The id is therefore stamped at each turn-root span helper. For the four message turns (command send/handle, event publish/handle) the id is already in scope on the message. The two fresh-root turns mint their id at span-start and thread it onto whatever they stage. The one turn that continues a *prior* conversation it cannot cheaply see is the dead-letter promotion: it runs on the non-throwing safety-net path and must not deserialize a failing — possibly unmaterializable — body to recover the id. So one additive wire field, `OutboxEntry.ConversationId`, carries the conversation id at the top level of the durable entry, stamped at construction (where it is already in scope) and read on the promote path without touching the body. This is what makes "the dead-letter row still names its conversation even when its body can no longer be decoded" true, and the persistence-axis conformance battery proves it against real stores precisely in that unmaterializable-body case.

## Not in scope (recorded so it is not lost)

The realistic entry points to live-production debugging are rarely "I already have a trace open" — they are "a metric alerted" or "I have a log line." OTel's bridges for those are **metric exemplars** (pivot from a histogram bucket to a representative trace) and **log↔trace correlation** (`trace_id` on the `LogRecord`). Whether Edict's instruments emit exemplars and whether its error logs carry trace context is a *separate* and arguably larger piece of the observability story; it is deliberately not folded into this change. The conversation-id attribute only matters once an operator has reached the trace world — these two bridges are how they get there, and they are flagged here as their own future work.

## Considered Options

- **Leave correlation out of OTel; navigate links only (status quo before this ADR)** — rejected. Link-following is the right mechanism for crossing conversation boundaries but a poor one for "give me this whole conversation at once"; the chain-stable id already exists, and withholding it from the one place an operator queries was an omission, not a principled boundary. ADR-0060 rejected *substrate-SDK* stitching, never this.
- **One originating conversation id threaded onto every descendant turn (so a single query returns schedule fires and saga compensations too)** — rejected. It fights ADR-0060's deliberate "a timer fire is a new conversation" decision and collapses the conversation scope into the business-operation scope, which already has a native carrier (the grain key). It would make the conversation id less useful at the altitude operators most need it.
- **A bespoke `edict.correlation_id` tag, keeping the house term** — rejected. Reinvents a standardized attribute and loses tooling interop. With no released consumers, aligning the house term to the spec's canonical noun is cheap and permanent.
- **Carry the id everywhere via OTel baggage + a `BaggageSpanProcessor`** — rejected as the *guarantee* mechanism. Baggage is a fine propagation aid, but the processor that materializes baggage onto spans is registered in the consumer's SDK setup, which a library cannot assume exists. Edict guarantees only what it sets directly, so the turn-root stamp is the contract; baggage may complement it but cannot be relied upon for queryability.
