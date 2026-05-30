---
name: edict-authoring
description: Use this skill when working on a consumer app built on Edict and adding a new feature — a new Command, Event, Command Handler, Event Handler, Saga, Projection Builder, or Table Projection Builder. Walks the role decision tree before any code is written.
---

# Authoring a feature in an Edict consumer app

Use this skill the moment you decide to add behaviour to an Edict consumer app. Pick the right grain role first; cross-checking the existing handler inventory is the second move, not the last.

## The role decision tree

A new feature is *always* one of these five roles. Pick deliberately.

- **Command Handler** — the Guid-keyed aggregate. Use when the new behaviour is a state transition the user (or another system) is *asking for*. Lives on `EdictCommandHandler<TState>`; named `{Name}CommandHandler`. Handles `EdictCommand` subclasses, mutates durable `State`, returns an `EdictCommandResult`, and may raise `EdictEvent`s.
- **Command Validator** — the precondition gate. Lives in the same activation turn as the handler, reads state, never mutates, and yields a `Rejected` result on failure. The line between Validator and `Handle` is mutation: knowable-from-current-state → Validator; only-knowable-while-mutating → `Handle`.
- **Event Handler** — the terminal side-effect grain. Use when something has *happened* and the consequence is external (email, HTTP call, non-Edict store). Lives on `EdictEventHandler`; named `{Name}EventHandler`. Never owns events, never calls `Raise` or `Dispatch`.
- **Saga** — the coordinator. Use when an event needs to fan into exactly one follow-up Command, possibly on a different aggregate. Lives on `EdictSaga<TProgress>`; one Command per handled Event via `Dispatch`. Do not reconstruct progress by replay; the durable `Progress` is the source of truth.
- **Projection Builder** — the in-grain read model. Use when a small, single-grain forward-only view is enough. Edict is event-driven, not event-sourced — projections only ever see events from subscription forward.
- **Table Projection Builder** — the external read model. Use when the read model grows beyond what fits comfortably in grain state. The durable row lives in an external store; the grain holds a transient last-touched-slot cache.

## Always check the inventory before authoring

Before you write the new class, call **`edict_list_handlers`** to see every existing handler in the solution, and **`edict_list_route_keys`** to see which Guid keys are taken. Two reasons:

- A handler for the Command or Event already exists. The right move is to extend it or wire to it, not to write a parallel one.
- The `[EdictRouteKey]` Guid you were about to mint already routes a different Command — that is a runtime collision and a silent bug.

These two MCP tools are the load-bearing trigger for this skill: invoke `edict_list_handlers` and `edict_list_route_keys` when adding a feature, before suggesting code.

## Naming and brand prefix

Consumer subclasses are `{Name}{Role}` — never `Grain`-suffixed. Examples: `OrderCommandHandler`, `OrderPaymentSaga`, `OrdersByStatusProjectionBuilder`. The `Edict`-prefix is reserved for the framework surface itself; do not add it to your subclasses.

## See also

- For the contract attributes (`[EdictRouteKey]`, `[EdictStream]`, `[EdictTelemeterized]`, MessagePack rules): see the `edict-contracts` skill.
- For wiring the new grain into `Program.cs`: see the `edict-silo-wiring` skill.
- For testing the new grain: see the `edict-testing` skill.
- For diagnosing failures in the new grain: see the `edict-diagnostics` skill.
