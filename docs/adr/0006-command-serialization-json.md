# Command/result serialization is Orleans JSON, not generated surrogates

**Status:** the JSON wire-format decision is **superseded by ADR 0007** (Orleans MessagePack, string keys). The *generated-surrogate prohibition* below still stands unchanged — hand-written serializer attributes were never the thing this ADR ruled out.

`Command`, `CommandResult` and `RejectionReason` live in `Edict.Abstractions`, which has no Orleans dependency (ADR 0005). They cross the grain boundary, so Orleans needs codecs for them. The intended design was for `Edict.Generators` to emit `[GenerateSerializer]` surrogates + `[RegisterConverter]` converters per concrete command.

**This is impossible.** Roslyn source generators never observe each other's output. Orleans' own serializer source generator runs as a sibling of `Edict.Generators`, so any `[GenerateSerializer]`/`[RegisterConverter]` type Edict generates is invisible to Orleans codegen and **no codec is ever produced** (proven at runtime: `CodecNotFoundException: Could not find a copier for type …Command`). The same ordering limit also means a generated grain interface and a generated `[GrainType]` are invisible to Orleans codegen.

Edict therefore serializes the closed contract surface with Orleans' **JSON serializer**, configured by `Edict.Core` (`EdictSerialization.AddEdictContractSerializer`) and applied on every silo and client. This is codegen-independent and keeps `Edict.Abstractions` Orleans-free.

A future contributor will be tempted to "add the obvious generated surrogates". Do not — it cannot work until Roslyn lets generators chain. The JSON serializer is the deliberate, robust choice, not an oversight.

## Considered Options

- **Generated `[GenerateSerializer]` surrogates** — rejected: generator-ordering makes them invisible to Orleans codegen; zero codecs emitted.
- **Hand-written surrogates in `Edict.Core` for the whole hierarchy** — works for the closed `CommandResult` (Core's own codegen sees them) but cannot cover consumer-defined concrete commands, which is the majority case. Rejected for non-uniformity.
- **Orleans JSON serializer scoped by predicate to the contract types** — chosen: one codegen-independent registration covers `Command`/`CommandResult`/`RejectionReason` and every consumer command, polymorphically.

## Consequences

- Grain addressing also cannot rely on a generated grain interface or generated `[GrainType]`. The runtime routes via the real `IEdictCommandHandler` interface plus the grain's full type name; the generated per-aggregate interface remains only a typed routing token.
- JSON is less compact than Orleans' binary codecs; acceptable for the command spine and revisitable if profiling demands it.
- Silos and clients must call `AddEdictContractSerializer()`. The sample app slice will wire this; this slice wires it in the TestCluster fixture.
