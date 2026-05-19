# Edict.Analyzers

Roslyn diagnostic analyzers for the Edict framework. Each analyzer fires at compile time and surfaces a `DiagnosticSeverity.Error` — there are no warnings, only hard contract violations.

All analyzers match Edict types by fully-qualified name; they carry no runtime reference to any Edict assembly (ADR 0005).

---

## EDICT00x Catalog

| ID       | Analyzer class                          | Trigger                                                                 | Message                                                                                                                  | ADR        |
|----------|-----------------------------------------|-------------------------------------------------------------------------|--------------------------------------------------------------------------------------------------------------------------|------------|
| EDICT001 | `GrainMustBePartialAnalyzer`            | Non-`partial` class deriving from `EdictCommandHandlerGrain` or `EdictProjectionBuilderGrain` | `'{0}' derives from EdictCommandHandlerGrain/EdictProjectionBuilderGrain and must be declared partial`                  | ADR 0005   |
| EDICT002 | `HandleReturnTypeAnalyzer`              | `Handle` method in an `EdictCommandHandlerGrain` subclass whose return type is not `Task<EdictCommandResult>` | `Handle method for '{0}' in '{1}' must return Task<EdictCommandResult>`                                                 | ADR 0004   |
| EDICT003 | `RouteKeyAnalyzer`                      | `EdictCommand` or `EdictEvent` subtype with zero, multiple, or a non-`Guid` `[EdictRouteKey]` property | `'{0}' has no [EdictRouteKey]` / `multiple [EdictRouteKey] properties` / `[EdictRouteKey] property '{0}' … must be of type Guid` | ADR 0011   |
| EDICT004 | `DuplicateCommandRouteAnalyzer`         | Two `Handle(TCommand)` overloads in different `EdictCommandHandlerGrain` subclasses for the same command type (compilation-end) | `'{0}' is already handled by '{1}'; each command must route to exactly one grain`                                        | ADR 0004   |
| EDICT005 | `TelemeterizedMustBePrimitiveAnalyzer`  | `[EdictTelemeterized]` placed on a property whose type is not a primitive (bool, byte, sbyte, char, short, ushort, int, uint, long, ulong, float, double, decimal, string, or Guid) | `[EdictTelemeterized] property '{0}' must be a primitive type …`                                                        | ADR 0003   |
| EDICT006 | `CommandMustBePartialAnalyzer`          | Non-`abstract`, non-`partial` class/record deriving from `EdictCommand`  | `'{0}' derives from EdictCommand and must be declared partial; the source generator emits the Orleans [Alias] into a second partial declaration` | ADR 0005   |
| EDICT007 | `EventMustBePartialAnalyzer`            | Non-`abstract`, non-`partial` class/record deriving from `EdictEvent`    | `'{0}' derives from EdictEvent and must be declared partial; the source generator emits the Orleans [Alias] into a second partial declaration`   | ADR 0005   |
| EDICT008 | `EventMustHaveStreamAnalyzer`           | Non-`abstract` `EdictEvent` subtype missing `[EdictStream(name)]`        | `'{0}' derives from EdictEvent and must be decorated with [EdictStream(name)]; omitting it causes silent stream misrouting` | ADR 0011   |
| EDICT009 | `ProjectionHandleSignatureAnalyzer`     | `Handle` method in an `EdictProjectionBuilderGrain` subclass returning `Task<T>` instead of `Task`, or whose parameter does not derive from `EdictEvent` | `Handle method for '{0}' in '{1}' must return Task, not Task<T>` / `… must take an EdictEvent-derived parameter`       | ADR 0004   |
