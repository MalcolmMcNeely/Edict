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

## When to look up a contract term

When a consumer asks "what counts as a Domain Stream?" / "what is a Route Key here?" / "what does Telemeterized mean on an Event?", or when picking between two terms whose distinction is fuzzy in their head, invoke **`edict_describe_glossary_term`** for the authoritative one-line definition and its `_Avoid_` list. The optional `Edict` prefix on the query is elidable — `Stream`, `Domain Stream`, and `EdictStream` all resolve. Use this before guessing a definition from the attribute name.

## Wire format: MessagePack-first, no `[Union]`

Edict contracts are MessagePack-serialised on the wire. Do **not** decorate a Command or Event with `[Union]` or treat the wire shape as JSON. Wire identity is the type's simple name; the generator emits `[Alias(nameof(TheCommand))]` so a rename is a wire break — that is intentional.

If you find yourself reaching for `[Union]` to model "command-or-this-other-command", that is two Commands, not one polymorphic Command. Split them.

A consumer never types `EdictEventEnvelope` — the receiver pipeline unwraps the wire envelope before dispatch. Do not derive consumer Events from `EdictEventEnvelope`, and do not name it on a `Handle` signature.

## When to look up the why

When a consumer asks "can we just use JSON?" or "why can't I add `[Union]`?" or "why is the wire identity the simple name?", invoke **`edict_lookup_adr`** to fetch the ADR that explains it. The relevant decisions:

- ADR-0006 — MessagePack wire format.
- ADR-0007 — `Edict.Contracts` boundary.
- ADR-0009 — Stable command wire identity.
- ADR-0010 — Event addressing model.
- ADR-0037 — `[EdictTelemeterized]` tag keys, no type prefix.
- ADR-0046 — Canonical authoring shape for messages and persisted state.

`edict_lookup_adr` is the load-bearing trigger for this skill: use it for any contract-attribute "why" question rather than guessing.

## See also

- For picking the role bound to the new contract: see the `edict-authoring` skill.
- For wiring the contract's silo support: see the `edict-silo-wiring` skill.
- For testing a workflow that exercises the new contract: see the `edict-testing` skill.
