# Edict.Contracts boundary

`Edict.Contracts` is the consumer's shared command/event surface: the contract types (`EdictCommand`, `EdictEvent`, `EdictCommandResult`, `EdictRejectionReason`, the attributes) and the abstractions both the client and silo bind to (`IEdictSender`, `IEdictTableRepository`, the projection readers). A consumer's own contracts assembly references only this, so it stays free of the Orleans **server** SDK.

It must **not** depend on the Orleans server runtime. Folding it into `Edict.Core` would force every assembly that merely *defines* a command to transitively acquire `Microsoft.Orleans.Sdk` and the grain bases — a one-way leak no analyzer inside a single assembly can stop. This is the `Orleans.Core.Abstractions` ↔ `Orleans.Core` split applied to Edict.

The boundary does **not** make any *deployment tier* Contracts-only. Sending a command needs the real `EdictSender` and `AddEdictContractSerializer`, both of which live in `Edict.Core` and bind the Orleans client — so a client tier that sends commands or reads projections references `Edict.Core`, exactly as the silo does. What lives on Contracts alone is the *shared contract assembly* both tiers consume, plus any consumer layer depending only on the abstractions (e.g. a Blazor WASM front-end sharing DTOs, where the server runtime cannot run). `IEdictSender` belongs here because it is the substitution seam `Edict.Testing` swaps.

The split is enforced by an architecture-test boundary (`EdictContracts_ShouldNotDependOnOrleansRuntime`), not by symbol-level analyzers.
