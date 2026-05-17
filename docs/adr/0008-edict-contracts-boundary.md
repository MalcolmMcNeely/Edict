# `Edict.Abstractions` becomes `Edict.Contracts`; the split is shared-kernel-vs-runtime, not Orleans-vs-no-Orleans

**Status:** accepted — corrects the rationale asserted in ADR 0005's and ADR 0006's prose

`Edict.Abstractions` is renamed **`Edict.Contracts`**. It holds the command/event/result contract surface plus the seam interfaces both tiers bind to — `Command`, `Event`, `CommandResult`, `RejectionReason`, `[RouteKey]`, `[Telemeterized]`, **`IEdictSender`**, and MessagePack annotations. `Edict.Core` keeps the grain runtime: grain bases, the `EdictSender` implementation, serializer wiring, the validation pipeline.

## Why the rename and re-justification

The two-assembly split was documented as "keep consumers Orleans-free." That rationale is now false (consumers may take Orleans; the contract assembly takes `MessagePack.Annotations`). The split is still load-bearing, for a different reason: the command/event contract is a **shared kernel between the command *producer* (Orleans client tier) and the *consumer* (silo tier)**, and they must not share the grain runtime. If the contract types lived in one assembly with `CommandHandlerGrain`, every project that merely *constructs* a command (the client/API tier, any shared domain-contracts assembly) would transitively acquire `Microsoft.Orleans.Server` and the grain bases — a one-way leak no analyzer inside a single assembly can stop. `IEdictSender` is the substitution seam `Edict.Testing` swaps, so its *interface* belongs with the contract, not in the runtime it abstracts.

## Considered Options

- **Fold `Abstractions` into `Core`** — rejected: the client/producer tier would transitively acquire the server-side grain runtime; the consumer cannot organise around a leak originating inside Edict.
- **Keep two assemblies, keep the "Orleans-free" name/rationale** — rejected: the rationale is now untrue and misleads future readers into "fixing" the wrong thing.
- **Rename to `Edict.Contracts`, re-base the rationale on shared-kernel-vs-runtime, move `IEdictSender` across** — chosen.

## Consequences

- Dependency direction is enforced by a new ArchUnitNET suite (`Edict.Architecture.Tests`): `Edict.Contracts` must not reference the Orleans *runtime*; the client tier must not reach grain bases. This is an assembly-level guarantee the symbol-level EDICT001–005 analyzers do not provide.
- `Edict.Generators` still references nothing and matches by fully-qualified name (ADR 0005 unaffected); only the FQNs change with the namespace rename.
- Every `Edict.Abstractions` reference in csproj/usings/CLAUDE.md and the auto-memory becomes stale and is updated in the same change.
