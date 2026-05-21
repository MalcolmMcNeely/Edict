# MessagePack wire format

`EdictCommand`, `EdictEvent`, `EdictCommandResult`, `EdictRejectionReason` and consumer-defined concrete commands/events serialize across the grain and stream boundary via Orleans' **MessagePack** serializer (`Microsoft.Orleans.Serialization.MessagePack`) using **string keys** (`[MessagePackObject(keyAsPropertyName: true)]`). The relevant prohibition on **generated** `[GenerateSerializer]` surrogates still stands — Roslyn generators cannot observe each other's output, so any Edict-generated Orleans-codec type is invisible to Orleans codegen and produces a runtime `CodecNotFoundException`; MessagePack attributes are hand-written (on the bases by Edict, on concrete types by the consumer per ADR 0022) so they never enter that trap. Polymorphism rides Orleans' external-serializer type manifest, so `[Union]` on the base is not required (essential — an open framework cannot enumerate consumer subtypes); drift is guarded by a build-time Verify snapshot of each contract's string-key shape, not a runtime ordinal-id scheme.

## Considered Options

- **Integer MessagePack keys + reserved 900+ range on the bases** — rejected: integer keys force every consumer DTO member to carry an explicit `[Key(n)]` and a new analyzer to make that safe, pushing ceremony onto every consumer for a compactness win the low-volume command spine does not need.
- **Orleans JSON serializer** (the prior choice) — superseded: JSON was chosen only because generated surrogates were impossible; hand-written MessagePack avoids that entirely and is more compact.
