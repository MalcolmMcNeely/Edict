---
name: edict-contracts
description: Use this skill when working on a consumer app built on Edict and defining or modifying a Command or Event contract — anything deriving from EdictCommand or EdictEvent. Covers RouteKey, Stream, Telemeterized, MessagePack-first, Alias, and the no-Union rule.
---

# Authoring Edict contracts

Every Command and Event is wire material. Get the attributes wrong and the analyzer (or the runtime) will tell you, but only after you have committed to a shape. Get them right up front.

## Smallest valid Command

```csharp
using Edict.Contracts.Commands;

public sealed partial record PlaceOrderCommand(
    [property: EdictRouteKey] Guid OrderId,
    string CustomerReference) : EdictCommand;
```

## Smallest valid Event

```csharp
using Edict.Contracts.Events;

[EdictStream("Orders")]
public sealed partial record OrderPlacedEvent(
    [property: EdictRouteKey] Guid OrderId) : EdictEvent;
```

## The attribute checklist

- **`[EdictRouteKey]`** — on the single `Guid` property that addresses the message. On a Command it picks the aggregate grain; on an Event it picks the stream key. Exactly one per type. Never use `[Key]`: that name belongs to `System.ComponentModel.DataAnnotations` and will collide. The Event's route key is independent of the Command's — a saga commonly re-keys across domains.
- **`[EdictStream("Name")]`** — on the concrete Event class. Names the domain stream the event belongs to; publisher and every subscriber are derived from this name. Required on every Event; omitting it is `EDICT008` at build time.
- **`[EdictTelemeterized]`** — on a primitive property of a Command or Event subclass. The generator emits code writing the property as an OpenTelemetry tag on the active span — `edict.{snake_case_property_name}` on the Command span for a Command, on both publish and handle spans for an Event. The tag key is shared across declaring types so the same domain concept queries by one key.
- **`partial`** — required on every concrete Command and Event; the generator emits the Orleans `[Alias]` into a second partial declaration (`EDICT007`). A concrete Event must have exactly one `[EdictRouteKey]` `Guid` property (`EDICT003`).
- **`[EdictSagaTimeout]`** — not on a Command or Event but on a **saga class** (`Edict.Contracts.Sagas`), declaring that saga's absolute lifetime cap. Either a duration literal `[EdictSagaTimeout("1.00:00:00")]` (where the leading field is **days**, so `"24:00:00"` is 24 days, not 24 hours), or `[EdictSagaTimeout(Unbounded = true)]` to opt out; never both. Absent, the saga inherits the silo-wide default (ships at 7 days). EDICT020 rejects a non-positive or non-parseable literal, EDICT021 rejects setting both `Duration` and `Unbounded`, and EDICT022 warns on an `OnSagaTimeoutAsync` override of an `Unbounded` saga. The saga lifecycle itself is the `edict-authoring` skill's territory.

## Correlation id is framework-stamped, not authored

Both `EdictCommand` and `EdictEvent` carry a `CorrelationId` the framework stamps and propagates for you — you do not declare it, attribute it, or set it. It is minted when a Command is first sent and inherited by every message that Command sets in motion, so it is the chain-stable id behind read-your-writes (it is what an `EdictCursor` wraps) and a grouping dimension on dead-letter rows. Do not add a correlation or causation field of your own to a contract, and do not confuse it with the `[EdictRouteKey]` Guid: the route key addresses one message and re-keys across domains, the correlation id stays constant along the whole chain. A caller *may* supply its own correlation when sending (it is honoured rather than overwritten), but the common path stamps it automatically.

## Principal is framework-stamped, not authored

When auditing is enabled, `EdictCommand` and `EdictEvent` also carry a **principal** — the actor on whose authority the message was issued. Like the correlation id it is a framework-managed durable field, **not** a contract property you declare: it is stamped once at the originating `SendAsync` from an edge resolver and inherited by every message that send sets in motion (a handler's raised Events, a saga's dispatched Commands). Do not add a principal, actor, or user field of your own to a Command or Event. The wiring that supplies it is the `edict-silo-wiring` skill; the compile-time aid (`EDICT023`) and the fail-closed `EdictMissingPrincipalException` are the `edict-diagnostics` skill. The principal is the *actor*, never the *data subject* (the person the data is about) — conflating the two is the classic modelling error, so keep `EdictPrincipal` strictly the actor.

## Tenant-scoping is on the route-key type, not the message

To wall an aggregate behind a tenant (a B2B company, a realm, whatever boundary the consumer draws), mark its **route-key type** with `[EdictTenantScoped]` (`Edict.Contracts.Tenancy`). The marker takes a `struct`, so a tenant-scoped aggregate keys on a small wrapper struct rather than a bare `Guid`:

```csharp
using Edict.Contracts.Commands;
using Edict.Contracts.Tenancy;

[EdictTenantScoped]
public readonly record struct EmployeeId(Guid Value);

public sealed partial record AddEmployeeCommand(
    [property: EdictRouteKey] EmployeeId EmployeeId,
    string FullName) : EdictCommand;
```

Every Command and Event keyed by that type now lives behind the **company wall**: its grain and stream keys compose as `{tenant}|{guid}` instead of the bare `{guid}` of a public aggregate. The marker sits on the route-key type, not on each message, because an aggregate is a cluster of messages sharing one route key. Declaring tenancy once on the type makes a leak-by-drift across that cluster unrepresentable, where a per-message attribute could be applied to four of five messages and leak on the fifth. A **public aggregate** (orders, say) keeps an unmarked route-key type and is never walled, so public and tenant-scoped aggregates coexist in one app.

The tenant id itself is framework-stamped and carried as a durable field, exactly like the correlation id and the principal: you never add a tenant field to a contract. How it is stamped at the origin and read back is the `edict-silo-wiring` and `edict-authoring` skills' territory; the `[EdictTenantScoped]` marker here is the only thing you author on the contract. An origin send of a tenant-scoped command with no tenant is caught at compile time by **`EDICT024`** (see the `edict-silo-wiring` skill) and fails closed at runtime as `EdictMissingTenantException`.

## When to look up a contract term

When a consumer asks "what counts as a Domain Stream?" / "what is a Route Key here?" / "what does Telemeterized mean on an Event?", or when picking between two terms whose distinction is fuzzy in their head, invoke **`edict_describe_glossary_term`** for the authoritative one-line definition and its `_Avoid_` list. The optional `Edict` prefix on the query is elidable — `Stream`, `Domain Stream`, and `EdictStream` all resolve. Use this before guessing a definition from the attribute name.

## Wire format: MessagePack-first, no `[Union]`

Edict contracts are MessagePack-serialised on the wire. Do **not** decorate a Command or Event with `[Union]` or treat the wire shape as JSON. Wire identity is the type's simple name; the generator emits `[Alias(nameof(TheCommand))]` so a rename is a wire break — that is intentional.

If you find yourself reaching for `[Union]` to model "command-or-this-other-command", that is two Commands, not one polymorphic Command. Split them.

## If auditing is on, audited message types are append-only

When the silo is audited (`silo.WithAudit()`), a captured Command or Event body is read back by deserializing it into the type you authored (`IEdictAuditRepository.GetMessageAsync`). The audit log is infinite-retention, so that makes every audited message type part of the permanent record's *readable* schema: deleting, renaming, or breaking-changing the type — or its generated `[Alias]` — silently severs the typed read of every record already captured under it. The stored bytes and their hash survive (`GetPayloadAsync` still returns them, and that is the integrity anchor), but the typed read throws `EdictAuditMessageDeserializationException`. So once a type has been audited, treat it as append-only: add fields, never remove or rename one. No analyzer enforces this — it cannot see your retention policy — it is a discipline auditing buys into.

A consumer never types `EdictEventEnvelope` — the receiver pipeline unwraps the wire envelope before dispatch. Do not derive consumer Events from `EdictEventEnvelope`, and do not name it on a `HandleAsync` signature. The `HandleAsync` overload that receives the contract takes **no** `public` modifier: the generator discovers it by name and the generated dispatch calls it from the same partial, so the keyword is redundant (the house rule omits it).

## When to look up the why

When a consumer asks "can we just use JSON?" or "why can't I add `[Union]`?" or "why is the wire identity the simple name?", invoke **`edict_lookup_adr`** to fetch the ADR that explains it. The relevant decisions:

- ADR-0006 — MessagePack wire format.
- ADR-0007 — `Edict.Contracts` boundary.
- ADR-0009 — Stable command wire identity.
- ADR-0010 — Event addressing model.
- ADR-0037 — `[EdictTelemeterized]` tag keys, no type prefix.
- ADR-0046 — Canonical authoring shape for messages and persisted state.
- ADR-0050 — Saga absolute lifetime cap (the `[EdictSagaTimeout]` attribute).
- ADR-0065 — Tenant as a routed identity axis (the `[EdictTenantScoped]` marker and the `{tenant}|{guid}` key fold).
- ADR-0067 — Tenant isolation enforcement and storage (the company wall the marker buys).

`edict_lookup_adr` is the load-bearing trigger for this skill: use it for any contract-attribute "why" question rather than guessing.

## See also

- For picking the role bound to the new contract: see the `edict-authoring` skill.
- For wiring the contract's silo support: see the `edict-silo-wiring` skill.
- For testing a workflow that exercises the new contract: see the `edict-testing` skill.
