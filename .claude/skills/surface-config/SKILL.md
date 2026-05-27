---
name: surface-config
description: Use this skill when introducing a tunable value (TimeSpan, int, magic string) in Edict.Core / Edict.Azure / Edict.Contracts framework code, or when authoring a new ISiloBuilder.AddEdict* extension. Surfaces the five-step ADR-0028 checklist so a new knob lands as an options property — never a literal in mechanism code.
---

# Surface-config

ADR 0028 pins down the principle: **every tunable knob the framework exposes is an options property with a default, a validation rule, and a sample line — never a literal in mechanism code.** This skill fires at authorship time so a new knob lands on the right surface from the start.

## When the principle applies

Trigger this skill the moment you:

- Introduce a `TimeSpan.FromMinutes(...)`, `TimeSpan.FromSeconds(...)`, `TimeSpan.FromMilliseconds(...)`, or other `TimeSpan` literal in `Edict.Core` / `Edict.Azure` / `Edict.Contracts` mechanism code.
- Introduce a numeric default in a `protected virtual int` / `protected virtual double` override.
- Introduce a `"name"` literal in mechanism code that an operator might reasonably want to rename (stream provider name, container name, table name).
- Author a new `Add*` extension on `ISiloBuilder` or extend one of the existing three.

If the literal is a frozen wire identity (alias values, OTel source name, dead-letter partition key), it stays a constant — see "When the principle does NOT apply" below.

## The five-step checklist

When a new knob surfaces, all five steps land in the same PR:

1. **Options property.** Add a property to `EdictOptions` (core), `EdictAzureStreamsOptions` (streams, wire-cap concerns), or `EdictAzurePersistenceOptions` (persistence, storage concerns). Flat, no nesting — IntelliSense surfaces every knob without a category-discovery step.

2. **Constructor default.** Set the property's default to the literal value previously hardcoded in mechanism code. The default is the documented baseline; tuning is opt-in.

3. **Validation rule.** Add a rule to `EdictOptionsValidator` (or the equivalent Azure validator). Validation throws at startup via the host's `EdictWiringValidator`. Never silently clamp — an out-of-range value the consumer typed is a typo, and the loud feedback path catches it before traffic flows.

4. **Sample line.** Add a line to `Sample.Azure.Silo/Program.cs` under the appropriate `silo.AddEdict*` lambda showing the literal default. The sample doubles as the config catalogue; a consumer who wants to learn what's tunable reads this file first.

5. **Provider marker (if applicable).** If the new knob lives on a brand-new provider extension (e.g. a Postgres persistence provider), register an `IEdictWiringMarker` implementation in `Edict.Contracts.Configuration` so the startup validator can detect the missing-provider call.

## CI enforcement

The principle is enforced by `Edict.Architecture.Tests` — `EdictMechanismCode_ShouldNotContainTimeSpanLiteralDefaults` scans `Edict.Core` and `Edict.Azure` and fails on any `TimeSpan.From*(literal)` outside an `*Options.cs` file. If you must add a `TimeSpan` literal in mechanism code, the right move is almost always to surface it through the options class instead.

## When the principle does NOT apply

These literals are wire identities, not tunable knobs, and stay as constants:

- `[Alias("...")]` strings on `[GenerateSerializer]` types — frozen ABI.
- The OTel `ActivitySource` name `"Edict"` — the framework's brand on the wire.
- Dead-letter partition key `"deadletter"`, drain reminder name `"edict-outbox-drain"` — internal wire identities downstream consumers and operators key off.
- Test fixture constants that aren't part of the consumer-facing surface (test cluster connection strings, test-only timeouts).

If you're unsure whether something is a knob or a wire identity, ask: "would a consumer ever want this different in production?" If yes, it's a knob — apply the five-step checklist. If the answer is "no — renaming this would break the wire," it's a constant.
