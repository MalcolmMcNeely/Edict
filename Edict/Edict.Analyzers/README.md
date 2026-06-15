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

### EDICT018 — `HandleMustBeHandleAsyncAnalyzer` (Error, has a code fix)
A Task-returning method literally named `Handle` on a class deriving from one of the four Edict consumer bases (Command Handler, Event Handler, Saga, Projection Builder). The generator discovers handlers by exact method name (`HandleAsync`); a method named `Handle` compiles cleanly but silently never fires at runtime, so the rule flips the silent no-op into a build error. The analyzer deliberately string-matches `"Handle"` rather than the shared `HandleMethodName` constant so it keeps catching the old name across future renames. `HandleMustBeHandleAsyncCodeFixProvider` renames the method in place.

### EDICT019 — `DuplicateCommandValidatorAnalyzer` (Error, compilation-end)
Two `EdictCommandValidator<TCommand>` derivatives bound to the same `TCommand`. Validators are auto-discovered via FluentValidation's `AddValidatorsFromAssemblies`, so a duplicate registration is last-wins at DI build time — the silent winner ships dead code. The rule turns that into a build error.

### EDICT020 — `SagaTimeoutDurationAnalyzer` (Error)
The `[EdictSagaTimeout("...")]` duration literal must parse (invariant culture) to a `TimeSpan` greater than zero. A typo arms nothing; a zero or negative cap would fire immediately or never. The reader parses the same literal at runtime, so a bad value otherwise mis-arms the cap reminder silently.

### EDICT021 — `SagaTimeoutUnboundedExclusivityAnalyzer` (Error)
A `[EdictSagaTimeout]` that sets both a duration and `Unbounded = true` is self-contradictory — one declares a finite cap, the other declares none. The runtime resolves the conflict silently in favour of unbounded, so the rule refuses the build.

### EDICT022 — `DeadSagaTimeoutHookAnalyzer` (Warning)
Overriding `OnSagaTimeoutAsync` on a saga declared `[EdictSagaTimeout(Unbounded = true)]` is dead code: an unbounded saga never arms a cap, so the hook can never fire. Only the explicit-unbounded case is detectable — the analyzer cannot see the runtime silo-wide default.

### EDICT023 — `OriginSendPrincipalAnalyzer` (Error, **opt-in** — off by default)
A bare origin send, `IEdictSender.SendAsync(command)`, has no principal. When a consumer adopts auditing the runtime fails closed at the origin if no principal resolves, and this analyzer moves that feedback left into the IDE. It is the one analyzer that is **off by default**: an analyzer runs per-compilation and cannot see whether `AddEdictAudit` was wired in another assembly (the silo/client project, typically a different compilation from the edge where the sends live), so it cannot tell a correctly-resolver-backed bare send from an un-attributable one. A consumer who audits turns it on per project through the `.editorconfig` knob `dotnet_diagnostic.EDICT023.severity = error` (no MSBuild surface), which is itself a compliance statement — hence `DefaultSeverity` is `Error` once enabled. There is **no code fix**: the right principal is a domain decision, and a placeholder would invite rubber-stamping. The explicit `SendAsync(command, principal)` overload is already attributed and never flagged; framework-relayed sends (saga `Dispatch`, outbox `SendCommand`, schedule fire) are distinct methods a consumer never writes, and `Raise` is an in-turn inherit, so an analyzer scanning consumer source sees only origin sends by construction. A resolver-backed site the consumer has confirmed is attributed at runtime is silenced with `[SuppressMessage("Edict", "EDICT023")]`.

---

## Numbering

IDs 010, 012, 013, 014 are unallocated gaps. Don't backfill; allocate the next free ID at the end of the range so existing diagnostics stay stable across releases.
