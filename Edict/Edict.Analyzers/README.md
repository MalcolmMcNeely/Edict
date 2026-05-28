# Edict.Analyzers

Roslyn diagnostic analyzers for the Edict framework. They fire at compile time, surface the violation at the call site, and — for the contract rules — refuse the build before broken code reaches a runtime that would surface the same problem as a stream misroute or a silent persistence drift.

No runtime reference to any Edict assembly (ADR 0005); types and attributes are matched by fully-qualified name through `EdictWellKnownNames` (compile-linked from `Edict.Generators`).

Two severities, two reasons:
- **Error** — a contract violation that breaks Orleans/MessagePack round-trip, dispatch, or persistence. The build must fail.
- **Warning** — a missed optimisation. The code still works (the registrar path or runtime dispatcher catches it), but the ADR-0034 interceptor fast path can't bind, so you'll quietly pay registrar-lookup cost forever.

---

## Catalog

### EDICT001 — `GrainMustBePartialAnalyzer` (Error)
A class deriving from `EdictCommandHandler`, `EdictEventHandler`, `EdictProjectionBuilder`, or `EdictSaga<TProgress>` must be declared `partial`. The generator emits the Orleans grain interface and dispatch spine into a second partial — without `partial`, the two halves can't merge and the grain never compiles.

### EDICT002 — `HandleReturnTypeAnalyzer` (Error)
The `Handle(TCommand)` method on an `EdictCommandHandler` subclass must return `Task<EdictCommandResult>`. Any other return type breaks the `DispatchAsync` switch the generator emits over each `Handle` overload.

### EDICT003 — `RouteKeyAnalyzer` (Error, three sub-rules under one ID)
An `EdictCommand` or `EdictEvent` subtype must declare exactly one `[EdictRouteKey]` property and it must be `Guid`. Zero, multiple, or non-Guid all fire — without a unique Guid route key, Orleans can't pick a grain.

### EDICT004 — `DuplicateCommandRouteAnalyzer` (Error, compilation-end)
Two `Handle(TCommand)` overloads in different `EdictCommandHandler` subclasses for the same command type. Each command must route to exactly one grain; ambiguity here would let the registrar pick a winner silently.

### EDICT005 — `TelemeterizedMustBePrimitiveAnalyzer` (Error)
`[EdictTelemeterized]` may only sit on a property whose type is a primitive (`bool`, the integer family, `float`/`double`/`decimal`, `string`, `Guid`). The interceptor bakes the tag setter as `Activity.SetTag(name, value)` — non-primitive payloads aren't safe to splat into OTel tags.

### EDICT006 — `CommandMustBePartialAnalyzer` (Error)
Non-abstract `EdictCommand` subtype must be declared `partial` so the generator's `[Alias]` + `[MessagePackObject(true)]` second partial can merge in. Without it, the ADR-0007 polymorphic round-trip silently breaks.

### EDICT007 — `EventMustBePartialAnalyzer` (Error)
Same rule, same reason, for `EdictEvent` subtypes.

### EDICT008 — `EventMustHaveStreamAnalyzer` (Error)
A non-abstract `EdictEvent` subtype must carry `[EdictStream(name)]`. Omitting it causes silent stream misrouting — the event lands on a default stream nobody is subscribed to, and you don't notice until production.

### EDICT009 — `ProjectionHandleSignatureAnalyzer` (Error)
The `Handle(TEvent)` method on an `EdictProjectionBuilder` subclass must return `Task` (not `Task<T>`) and take a parameter deriving from `EdictEvent`. The projection dispatcher can't yield a return value, and a non-event parameter would never receive a callback.

### EDICT011 — `PersistedStateContractAnalyzer` (Error, four sub-rules under one ID — has a code fix)
Enforces the consumer-owned half of the attribute-placement contract on every `IEdictPersistedState` implementer. The generator owns alias/serializer attributes on commands and events (safe to recompute every build), but persisted state must survive class renames — so the consumer is on the hook for:

- `MissingGenerateSerializer` — the type must carry `[GenerateSerializer]`.
- `MissingAlias` — the type must carry `[Alias("literal")]`.
- `AliasNotStringLiteral` — the `[Alias]` argument must be a frozen string literal. `nameof(T)` defeats the rename-survival guard the rule exists to enforce; write the literal directly.
- `PropertyMissingId` — every *declared* (not inherited) public instance property must carry `[Id(n)]`.

`PersistedStateContractCodeFixProvider` ships a quick-fix that drops in the missing attributes.

### EDICT015 — `BaseTypedSendAnalyzer` (Warning)
`IEdictSender.Send` was called with a base-typed argument (an `EdictCommand`-typed variable). The ADR-0034 interceptor matches per-concrete-command-type, so an abstract argument forfeits the fast path and runs the registrar dictionary lookup forever. Re-type the variable or cast at the call site.

### EDICT016 — `BaseTypedRaiseAnalyzer` (Warning)
Same shape, for `EdictCommandHandler.Raise(EdictEvent)`. Abstract argument means the typed `RaiseFast<TEvent>` stub can't bind.

### EDICT017 — `BaseTypedSagaDispatchAnalyzer` (Warning)
Same shape, for `EdictSaga<TProgress>.Dispatch(EdictCommand)`. Abstract argument means the typed `DispatchFast<TCommand>` stub can't bind.

---

## Numbering

IDs 010, 012, 013, 014 are unallocated gaps. Don't backfill; allocate the next free ID at the end of the range so existing diagnostics stay stable across releases.
