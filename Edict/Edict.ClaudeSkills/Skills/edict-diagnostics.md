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

Each `EdictDeadLetterEntry` carries the failed `Kind` (`PublishEvent` / `SendCommand` / `UpsertRow` / `InvokeHandler`), the `AttemptCount`, the `DeadLetteredAt`, the `SourceGrainKey` and `SourceGrainType`, the `EffectTarget` (encoded per kind), the captured `TraceParent`, the `ExceptionType`, the `Reason`, the `PayloadJson`, the `SourceEventId`, an optional `ClaimCheckKey`, and a `FailureKind` discriminator (`EffectFailure` vs `BlobMissing`).

There is **no `RedriveAsync`**. Recovery is manual: re-emit the source Command or saga step for `PublishEvent` and `SendCommand`; repair the destination row by hand for `UpsertRow`, informed by the row's contents. Dead-lettering is forensic surface, not back-pressure or retry — do not treat it as a recovery mechanism.

Never read the underlying `deadletter` table directly. Always go through `IEdictDeadLetterRepository`.

## Stitch the trace

The captured `TraceParent` on a dead-letter row preserves the W3C trace from the original effect. Pasting that trace id into the consumer's observability stack will join the dead-letter span to the originating Command span and any prior event-handle span — the chain is by trace context, not by the Command/Event Guid. The Guid is routing; trace is causality. A saga commonly re-keys, so the Guid will not stitch across domains.

When investigating a missing event or unrouted Command, the trace is what stitches the chain. The framework opens a single `ActivitySource` named `"Edict"`; subscribe to that and the spans (`edict.command`, `edict.event.publish`, `edict.event.handle`) carry the causality.

## Common failure shapes

- **`PublishEvent` dead-lettered with `FailureKind = BlobMissing`** — a claim-checked event's blob was reaped before delivery. The original `ClaimCheckKey` is preserved on the row; the event payload itself is gone. Claim-check blobs are append-only on purpose; if you see this, something is deleting blobs out-of-band.
- **`InvokeHandler` dead-lettered** — a consumer Event Handler threw past `MaxAttempts`. Read `Reason` and `ExceptionType` on the row; the failure is in the consumer's `HandleAsync` body.
- **`SendCommand` dead-lettered** — a saga's follow-up Command exhausted attempts. The target aggregate is unavailable or its `HandleAsync` is rejecting durably. Saga progress is still readable via `GetSagaProgress` in tests; in production, query the saga grain directly.
- **`UpsertRow` dead-lettered** — a Table Projection's write to the external store kept failing. Read the row's contents to repair the destination by hand.
- **Aggregate intake is not blocked** — dead-lettering is forensic-only. A permanently failing effect does not stall its source aggregate. If a Command Handler appears stuck, the cause is not dead-lettering.

## When to look up the why

For any "why does dead-letter behave this way?" or "why no redrive?" question, invoke **`edict_lookup_adr`**. The relevant decisions:

- ADR-0015 — Outbox engine (the host, the slice, the drain).
- ADR-0018 — Dead letter (forensic-only, table-projection-backed).
- ADR-0019 — Deferred dispatch (why `SendCommand` is an Outbox effect, not an inline call).
- ADR-0020 — Claim check (and the `BlobMissing` failure kind).
- ADR-0003 — Parent/child spans across the stream hop.
- ADR-0041 — Exception policy.

`edict_lookup_adr` is the load-bearing trigger for this skill: use it for any dead-letter, outbox, or trace "why" question rather than guessing.

## When MCP results look off

If a Dead Letter query returns empty when you know rows exist, or `edict_list_handlers` returns nothing when handlers are obviously present, the MCP server may have indexed the wrong workspace. Invoke **`edict_describe_mcp_state`** before re-running the lookup — it reports the loaded solution path, the indexed-handler count, and the registered tool list. A mismatch between the reported solution and the consumer's actual workspace explains the surprising empty result, and the `--solution` override in `.mcp.json` is the documented fix.

## See also

- For the testing surface that surfaces dead-letter rows on `Timeline` and `GetProjectionRow`: see the `edict-testing` skill.
- For the contract attributes that stamp the trace context: see the `edict-contracts` skill.
- For the role bound to the failing grain: see the `edict-authoring` skill.
