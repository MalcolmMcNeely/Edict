# Edict.Generators

The single Roslyn incremental source generator for the Edict framework. Carries no runtime reference to any Edict assembly (ADR 0005); base types and annotations are matched by fully-qualified name via `EdictWellKnownNames`.

---

## Layout

- `EdictGenerator.cs` — the single `[Generator]` orchestrator (ADR 0033)
- `Classification/` — `EdictTypeClassifier` and `EdictTypeKind` (single source of truth for "what kind of Edict type is this?")
- `Commands/`, `Events/`, `EventHandler/`, `EventStreamAccessors/`, `Projections/`, `Sagas/` — concept folders mirroring `Edict.Core`'s topology (ADR 0012). Each holds `{Concept}Discovery.cs`, `{Concept}Model.cs`, and one `{Concept}{Artefact}Emitter.cs` per emitted file.
- `Shared/` — cross-concept emitters (`SharedAliasEmitter` serves both commands and events; the templates are identical text).
- `EdictWellKnownNames.cs` — assembly-root FQN constants; compile-linked into `Edict.Analyzers`.

---

## Emitted artefacts

| Concept                | File pattern                                                | Trigger                                                                                       |
|------------------------|-------------------------------------------------------------|-----------------------------------------------------------------------------------------------|
| Command record         | `{Namespace}.{CommandName}.Alias.g.cs`                       | `partial record` deriving from `Edict.Contracts.Commands.EdictCommand`                          |
| Command handler grain  | `{Namespace}.{GrainName}.g.cs`                               | `partial class` deriving from `Edict.Core.Commands.EdictCommandHandler[<TState>]`                |
| Command route registrar| `Edict.Generated.EdictRouteRegistrar.g.cs`                   | One per assembly that has at least one command handler grain                                    |
| Event record           | `{Namespace}.{EventName}.Alias.g.cs`                         | `partial record` deriving from `Edict.Contracts.Events.EdictEvent`                              |
| Event stream registrar | `Edict.Generated.EdictEventStreamRegistrar.g.cs`             | One per assembly that has at least one `EdictEvent` with `[EdictStream]` + single `Guid [EdictRouteKey]` |
| Event handler grain    | `{Namespace}.{GrainName}.EventHandler.g.cs`                  | `partial class` deriving from `Edict.Core.EventHandler.EdictEventHandler`                       |
| Projection grain       | `{Namespace}.{GrainName}.g.cs`                               | `partial class` deriving from `Edict.Core.Projections.EdictProjectionBuilder`                   |
| Saga grain             | `{Namespace}.{GrainName}.Saga.g.cs`                          | `partial class` deriving from `Edict.Core.Sagas.EdictSaga<TProgress>`                            |
