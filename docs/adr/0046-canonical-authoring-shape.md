# Canonical authoring shape for messages and persisted state

Edict has two shapes a consumer hand-authors over and over: **messages** (concrete `EdictCommand` / `EdictEvent` subclasses) and **persisted state** (`IEdictPersistedState` implementations — aggregate state, saga progress, table-projection row POCOs). The wire and persistence contracts for each are already pinned (ADRs 0006/0009/0010 for messages, 0022 for state). What is not pinned is the C# *authoring* form. Until this ADR, the Sample showed messages as primary-constructor records with every property redeclared in the body — four properties greyed out by the IDE on `AddLineItemCommand`, three of them syntactically redundant. The consumer-shipped skill `edict-contracts.md` and `docs/usage/concepts/commands.md` carried the same redundant form, so a consumer who consulted the docs landed at the wrong shape.

This ADR pins the authoring form for both shapes.

**Messages are records authored as a primary constructor with attributes attached via `[property: ...]`** (Option D below). The primary-ctor parameters are the only place each property is named; attributes ride on the parameter via the `[property: ...]` target. Persisted state is a `sealed class` with `{ get; set; }` properties — the framework's authoring contract for state is in-place mutation, so a record's `with`-rewrite friction works against the documented idiom (`State.Status = ...`, `Progress.Stage = ...`).

No analyzer enforces either rule. The wire shape is identical across the message-authoring options, the generator walks `IPropertySymbol` regardless of declaration form, and Roslyn's IDE-level rules already grey body-redeclared primary-ctor parameters. An Edict-branded analyzer would add maintenance cost for a stylistic-only signal the editor already produces. This aligns with the existing "wire-shape Verify is the only stability guarantee" policy (no reflection-based shape tests on `EdictCommand` / `EdictEvent` / attributes).

The class-vs-record rule scopes to `IEdictPersistedState` implementations only. Other DTOs — `MetricsSnapshot` returned from a probe grain, ad-hoc DI-resolved data carriers, anything that is not a message and not framework-owned durable state — are out of scope and their author picks the shape that fits.

## Considered Options

### Message authoring form

- **A — primary-ctor params only, no body, no attributes.**
  ```csharp
  public sealed partial record PlaceOrderCommand(Guid OrderId, string CustomerReference) : EdictCommand;
  ```
  Rejected: non-starter. There is nowhere to attach `[EdictRouteKey]` or `[EdictTelemeterized]`. Every Edict message needs at least the route key.

- **B — primary-ctor + redeclare every property in the body.** (The current Sample form, prior to this ADR.)
  ```csharp
  public sealed partial record PlaceOrderCommand(Guid OrderId, string CustomerReference) : EdictCommand
  {
      [EdictRouteKey]
      public Guid OrderId { get; init; } = OrderId;

      public string CustomerReference { get; init; } = CustomerReference;
  }
  ```
  Rejected: every body property is a redeclaration the C# compiler has already synthesised from the primary-ctor parameter. The IDE greys the redundant ones. A consumer reading the type cannot tell whether the redeclaration is mandatory Edict ceremony or stylistic — the dominant feedback signal that forced this ADR.

- **C — primary-ctor + redeclare only attributed properties.**
  ```csharp
  public sealed partial record PlaceOrderCommand(Guid OrderId, string CustomerReference) : EdictCommand
  {
      [EdictRouteKey]
      public Guid OrderId { get; init; } = OrderId;
  }
  ```
  Rejected: minimum-ceremony body, but splits the property surface across two syntactic homes — attributed properties in the body, unattributed properties in the primary-ctor parameter list. A reader has to look in both places to enumerate the wire shape. The split is worse than the redundancy of B.

- **D — primary-ctor + `[property: ...]` for attributes.** **Chosen.**
  ```csharp
  public sealed partial record PlaceOrderCommand(
      [property: EdictRouteKey] [property: EdictTelemeterized] Guid OrderId,
      string CustomerReference) : EdictCommand;
  ```
  Single home for every property. No shadowing. Attributes live inline next to the parameter they describe. The IDE has nothing to grey because nothing is redundant. The wire shape is identical to A/B/C — MessagePack reads `[MessagePackObject(keyAsPropertyName: true)]` and serialises by property name; the generator's `CommandDiscovery.MapCommand` walks `IPropertySymbol` regardless of declaration form.

### Persisted-state authoring form

- **Record with `init` setters.**
  ```csharp
  public sealed record OrderState : IEdictPersistedState
  {
      [Id(0)]
      public OrderStatus Status { get; init; } = OrderStatus.Open;
  }
  ```
  Rejected: the framework's authoring contract for `IEdictPersistedState` is in-place mutation inside `HandleAsync` methods. With `init` setters, a consumer would have to write `State = State with { Status = ... }` per mutation — but `State` on the grain base is not user-writable. The record shape fights the documented idiom.

- **Class with `init` setters.** Rejected for the same reason as the record case — `init`-only setters block the in-place mutation the framework already documents in `OrderState`'s xmldoc.

- **`sealed class` with `{ get; set; }`.** **Chosen.** Matches the in-place mutation idiom across aggregate state, saga progress, and projection rows. `sealed` because none of the three roles' state is designed to be subclassed.

### No-analyzer rationale

- **Add an Edict analyzer (e.g. `EDICT016`) that flags body-redeclared primary-ctor parameters on `EdictCommand` / `EdictEvent` subclasses.** Rejected. The wire shape is identical across B/C/D, the generator walks `IPropertySymbol` and accepts all three, and the IDE already greys the redundant B case via Roslyn's built-in rules. An Edict-branded analyzer adds maintenance cost (analyzer code + tests + diagnostic-ID burn) for a stylistic-only signal the editor already provides. Aligns with the existing policy that wire-shape Verify is the only contracts-stability test.

### Why a stand-alone ADR rather than amending 0010 or 0022

The rule spans both the message wire-format chain (0006 / 0009 / 0010) and the persisted-state contract (0022). It has no clean home inside either. Folding it into 0010 would leave the persisted-state half stranded; folding it into 0022 would leave the message half stranded. A stand-alone ADR is the cheapest home for a cross-cutting rule. Cross-references can be added to 0010 and 0022 later if either is re-edited for unrelated reasons.

## Consequences

- `Sample/Sample.Contracts/**/*Command.cs` (10 files) and `**/*Event.cs` (11 files) are authored in Option D — no body redeclarations of primary-ctor parameters, attributes via `[property: ...]`.
- The consumer-shipped `edict-contracts` skill's "Smallest valid Command" and "Smallest valid Event" examples use Option D, and ADR 0046 appears in the skill's "When to look up the why" list.
- The concept docs at `docs/usage/concepts/commands.md` and `docs/usage/concepts/events.md` use Option D in their code samples.
- The auto-injected `csharp` skill carries a "Message authoring shape" subsection prescribing Option D and a "Persisted state authoring shape" subsection confirming `sealed class` + `{ get; set; }`, so future edits to this repo land on the canonical form by default.
- The class-vs-record rule reaches `IEdictPersistedState` implementations only. `MetricsSnapshot` and any other non-message non-state DTO stays whatever shape its author picks. The new ADR's scope note heads off the "should this be a class?" question for those types.
- Wire shape is unchanged across the B → D migration. The existing wire-shape Verify snapshots in `Edict.Core.Tests/Serialization/` are the regression net.
- No new analyzer, no new test, no generator change. The rule lives in ADR + skill bodies + canonical Sample.
