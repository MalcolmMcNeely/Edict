# Contract surface serializes as Orleans MessagePack (string keys), superseding ADR 0006's JSON

**Status:** accepted — supersedes ADR 0006's wire-format decision (ADR 0006's *generated-surrogate prohibition* still stands)

`Command`, `CommandResult`, `RejectionReason` and consumer-defined concrete commands serialize across the grain boundary via Orleans' **MessagePack** serializer (`Microsoft.Orleans.Serialization.MessagePack`) using **string keys** (`[MessagePackObject(keyAsPropertyName: true)]`), not the Orleans JSON serializer ADR 0006 chose.

## Why the flip

ADR 0006 reached for JSON because the *intended* design — `Edict.Generators` emitting `[GenerateSerializer]` surrogates — is impossible: Roslyn generators never observe each other's output, so Orleans codegen never sees Edict-generated types (the `CodecNotFoundException`). That prohibition is real and **unchanged**. But MessagePack attributes are **hand-written in source** — on the base types by Edict, on concrete commands by the consumer — so they never enter the generator-ordering trap. The trap only ever applied to *generated* Orleans attributes, not hand-authored ones. JSON was a workaround for a constraint MessagePack simply doesn't hit.

## Considered Options

- **Orleans JSON serializer (ADR 0006)** — superseded: chosen only because generated surrogates were impossible; MessagePack hand-written attributes avoid that entirely and are more compact.
- **Integer MessagePack keys + reserved 900+ range on the bases** — rejected: integer keys require *every* consumer DTO member to carry an explicit `[Key(n)]` and a new analyzer to make that safe, pushing ceremony onto every consumer for a compactness win the low-volume command spine does not need.
- **String MessagePack keys (`keyAsPropertyName: true`)** — chosen: zero per-property ceremony, no reserved range, no key analyzer; drift is guarded by a Verify schema-snapshot test that fails CI when a contract property is renamed or removed.

## Consequences

- Polymorphism: `IEdictSender.Send(Command)` takes the abstract base. Orleans' external-serializer integration writes the concrete type identity via its own type manifest and delegates only the body to MessagePack, so MessagePack never sees an abstract type and `[Union]` on the base is **not** required (essential — an open framework cannot enumerate consumer subtypes). This is load-bearing and must be proven by a TestCluster round-trip spike before building on it.
- Drift guard is a build-time Verify snapshot of each concrete command's string-key shape, not a runtime ordinal-id scheme.
- Silos and clients call the MessagePack registration (replacing `AddEdictContractSerializer`'s JSON wiring); the rename in ADR 0008 moves where it lives.
