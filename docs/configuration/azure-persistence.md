# Azure persistence configuration

`EdictAzurePersistenceOptions` backs `AddEdictAzurePersistence`, the single extension that chains Orleans Azure grain-storage, reminders, and table-storage plus the Edict provider seam (the table write-store factory). For the `Add*` call shape, the client-side setup, the four internally-wired Orleans pieces, and the framework-author gotchas, see [wiring/azure-persistence.md](../usage/wiring/azure-persistence.md).

## `EdictAzurePersistenceOptions`

| Property | Default | Purpose |
| --- | --- | --- |
| `GrainStateContainerName` | `"edict-state"` | Azure Blob container holding the Edict grain-state slot. Single-blob ETag atomicity covers the `[PersistentState("state", "edict-state")]` slot every framework grain base writes through. |
| `AuditTableName` | `"edictauditrecord"` | Azure Table holding the audit chain, written as one fan-out append row per access path (correlation, principal, entity) so each query is a single partition scan. The chain is tamper-**evidence** via its per-aggregate hash chain, which `VerifyEntityChainAsync` re-walks to surface an altered record. Note the honest limitation: unlike the Postgres chain (a WORM trigger that refuses an `UPDATE`/`DELETE` outright), the Azure-Table chain has no infra tamper-**prevention** until the deferred blob-sealing slice — a privileged operator can rewrite a row, and detection rests entirely on the hash chain. |
| `AuditPayloadContainerName` | `"edict-audit-payload"` | Azure Blob container holding the captured message bodies, one write-once blob per audit record id. Append-only by upload contract (`overwrite: false`); container-level immutability sealing is the deferred blob-sealing slice. |
| `TableServiceClient` | `null` | Optional `TableServiceClient`. A DI-registered singleton instance takes precedence so an `AddAzureClients()`-style power-user setup works without double-registration. |
| `BlobServiceClient` | `null` | Optional `BlobServiceClient` for grain-state blobs. Same DI-precedence rule. |

## Connection strings

Both clients (`TableServiceClient`, `BlobServiceClient`) can come from three places — whichever is set wins, in this order:

1. A DI-registered singleton client instance.
2. The matching `*Client` property on `EdictAzurePersistenceOptions`.
3. Neither — wiring throws `EdictWiringException` at `silo.AddEdictAzurePersistence`.

Local development uses Azurite via `UseDevelopmentStorage=true`. Production uses an Azure Storage account connection string or a `TokenCredential`-authenticated client. The two clients can point at the same account or split across accounts — table-storage limits (e.g. partition-throughput throttling) and blob-storage limits are independent, so a hot system can scale them separately.

Note that Azurite accepts table names Azure rejects: Azure Table names must match `^[A-Za-z][A-Za-z0-9]{2,62}$` — no hyphens, no underscores. The framework's literal dead-letter table `deadletter` is Azure-compliant; any projection `TableName` you author must be too. The wiring page covers this and the Azurite-fidelity caveats in full.

## See also

- [index.md](index.md) — the installation surface and fail-fast validation behaviour.
- [wiring/azure-persistence.md](../usage/wiring/azure-persistence.md) — the `Add*` call shape, client setup, and the dead-letter / Azurite-fidelity gotchas.
- [core.md](core.md) — the provider-agnostic `AddEdict()` knobs.
- [concepts/audit-log.md](../usage/concepts/audit-log.md) — what the `Audit*` knobs back: capture, attribution, the per-aggregate chain, and the Azure tamper-evidence-without-prevention honesty.
- ADRs — [0021 — Grain state on blob substrate](../adr/0021-grain-state-on-blob-substrate.md), [0023 — Config surface and installation](../adr/0023-config-surface-and-installation.md), [0042 — Azure package split](../adr/0042-azure-package-split.md).
