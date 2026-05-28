# Edict.Generators

The one Roslyn incremental source generator for the Edict framework. A single `[Generator]` — `EdictGenerator` (ADR 0033) — registers every output pipeline. It carries no runtime reference to any Edict assembly (ADR 0005); base types and attributes are matched purely by fully-qualified name via `EdictWellKnownNames` (compile-linked into `Edict.Analyzers`).

This doc tells you, for each emitted file, what the generator was looking at and what it wrote — so when you see a `*.g.cs` show up under a project, you can map it back to the trigger.

---

## Layout

- `EdictGenerator.cs` — the single orchestrator; all pipelines are registered here.
- `Classification/` — `EdictTypeClassifier` + `EdictTypeKind`. The single source of truth for "what kind of Edict type is this `RecordDeclarationSyntax` / `ClassDeclarationSyntax`?". Every discovery module calls it before doing anything else.
- One folder per concept (`Commands/`, `Events/`, `EventHandler/`, `EventStreamAccessors/`, `Projections/`, `Sagas/`), each holding `{Concept}Discovery.cs`, `{Concept}Model.cs`, and the per-artefact emitters. Mirrors `Edict.Core`'s topology (ADR 0012).
- `Shared/SharedAliasEmitter.cs` — the command-and-event alias template is byte-for-byte identical, so one emitter serves both.
- `EquatableArray.cs` — collection wrapper that gives incremental-generator-friendly value equality on model records.

---

## What gets generated

### Commands

**Per command record — `{Namespace}.{CommandName}.Alias.g.cs`**
Triggered by a non-abstract `partial record` deriving from `EdictCommand`. Emits a second partial decorated with Orleans `[Alias("{SimpleName}")]` and MessagePack `[MessagePackObject(true)]`. The simple-name alias is what makes the ADR-0007 polymorphic round-trip work, and `MessagePackObject(true)` opts the record into key-as-property-name serialisation so consumers never write `[Key(n)]` themselves.

**Per command-handler grain — `{Namespace}.{GrainName}.g.cs`**
Triggered by a `partial class` deriving from `EdictCommandHandler` or `EdictCommandHandler<TState>`. Emits the Orleans grain interface `I{GrainName}` (extending `IEdictCommandHandler`) and the `DispatchAsync(EdictCommand)` switch — one arm per concrete `Handle(TCommand)` overload — routing each command type through `ValidateAndHandleAsync(c, () => Handle(c))`. The handler stays free of dispatch boilerplate.

**Per assembly with handlers — `Edict.Generated.EdictRouteRegistrar.g.cs`**
Assembly-level registrar mapping every command type to its grain interface, so `IEdictSender.Send(EdictCommand)` can resolve a target without reflection at runtime. Only emitted if the assembly has at least one handler.

### Events

**Per event record — `{Namespace}.{EventName}.Alias.g.cs`**
Same shape as the command alias — non-abstract `partial record` deriving from `EdictEvent` gets the `[Alias]` + `[MessagePackObject(true)]` second partial. Same `SharedAliasEmitter`.

**Per assembly with stream events — `Edict.Generated.EdictEventStreamRegistrar.g.cs`**
Walks every `EdictEvent` carrying `[EdictStream(name)]` plus a single `Guid`-typed `[EdictRouteKey]` property. Emits an accessor map (`Dictionary<Type, EdictEventStreamAccessor>`) keyed by event type with `(streamName, evt => ((TEvent)evt).RouteKey)` entries, plus an `[assembly: EdictEventStreams(typeof(...))]` attribute pointing at the registrar. The runtime uses this to drop events on the right stream without per-event reflection.

### Event handlers

**Per event-handler grain — `{Namespace}.{GrainName}.EventHandler.g.cs`**
Triggered by a `partial class` deriving from `EdictEventHandler`. Emits the grain interface plus the spine that wires implicit stream subscription to the handler's `Handle(TEvent)` overloads.

### Projections

**Per projection grain — `{Namespace}.{GrainName}.g.cs`**
Triggered by a `partial class` deriving from `EdictProjectionBuilder`. Emits the grain interface + dispatch spine for the projection's `Handle(TEvent)` overloads.

### Sagas

**Per saga grain — `{Namespace}.{GrainName}.Saga.g.cs`**
Triggered by a `partial class` deriving from `EdictSaga<TProgress>`. Emits the grain interface + dispatch spine for the saga's `Handle(TEvent)` overloads.

### Interceptors (ADR-0034 fast path)

These three pipelines use C# 12 `[InterceptsLocation]` to rewrite concrete-typed call sites into typed fast paths that bypass the registrar dictionary lookup and stay monomorphic. Gated by the MSBuild property `EdictInterceptorsEnabled` (default on; set `false` to skip emission entirely — the registrar paths still work).

**Per command type — `{CommandFqn}.SendInterceptor.g.cs`**
For every `IEdictSender.Send(MyCommand)` call site with a concrete-typed argument, emits a `file static class` whose method carries one `[InterceptsLocation]` per discovered call site and forwards into `EdictSender.SendFastPathAsync<TCommand>` — passing the route-key value, command/grain names, and any `[EdictTelemeterized]`-decorated tag setters baked in as a static lambda. One file per command type, regardless of how many call sites bind to it. (Call sites with abstract `EdictCommand`-typed arguments fall back to the registrar; `EDICT015` warns about them.)

**Per event type — `{EventFqn}.RaiseInterceptor.g.cs`**
Same shape, for `EdictCommandHandler<TState>.Raise(MyEvent)` call sites. Forwards into `RaiseFast<TEvent>` so the buffer-add and `OccurredAt` stamp stay typed. (`EDICT016` warns on abstract-typed `Raise` arguments.)

**Per command type — `{CommandFqn}.DispatchInterceptor.g.cs`**
Same shape, for `EdictSaga<TProgress>.Dispatch(MyCommand)` call sites. Forwards into `DispatchFast<TCommand>` and preserves the single-command-per-event guard from the base method. (`EDICT017` warns on abstract-typed `Dispatch` arguments.)

---

## Why one generator and not seven

Pre-ADR-0033 there was one `[Generator]` per concept. Roslyn re-runs every registered generator on every source change, and a partial-record edit was billing six generators that had nothing to say about it. Folding to one orchestrator with per-concept pipelines kept the topology of the codebase intact (the `Commands/`, `Events/`, ... folders) while letting Roslyn batch the incremental work properly. Per-concept Verify snapshots in `Edict.Generators.Tests` are the regression net.
