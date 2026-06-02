# Azure streaming configuration

Two options classes back the Azure streaming side: `EdictAzureStreamsOptions` on `AddEdictAzureStreams` (the Orleans Azure Queue Storage stream provider plus the claim-check threshold) and `EdictAzureBlobClaimCheckOptions` on `AddEdictAzureBlobClaimCheck` (the Azure-blob-backed claim-check store). For the `Add*` call shape, the client-side setup, and the framework-author gotchas, see [wiring/azure-streaming.md](../usage/wiring/azure-streaming.md).

## `EdictAzureStreamsOptions`

| Property | Default | Purpose |
| --- | --- | --- |
| `StreamProviderName` | `"edict"` | Orleans stream-provider name. The runtime is pinned to `"edict"`; do not change. |
| `ClaimCheckThresholdBytes` | `30 720` | Serialised inner-event byte length above which the outbox uploads the body to the claim-check store and ships a pointer envelope on the wire. Default is ~2 KB of headroom under the 32 KB Azure Queue per-property cap to absorb envelope framing. |
| `QueuePollingPeriod` | `10 ms` | Azure Queue pulling-agent poll period. Hard floor on per-event latency — the consumer cannot observe an event until the next poll tick after the publisher's queue PUT. See the cost trade-off below. |
| `NumQueues` | `16` | Number of Azure queues the stream provider fans out across. See the cost trade-off below. |
| `QueueServiceClient` | `null` | Optional `QueueServiceClient`. A DI-registered singleton instance takes precedence so an `AddAzureClients()`-style power-user setup works without double-registration. |

## `EdictAzureBlobClaimCheckOptions`

| Property | Default | Purpose |
| --- | --- | --- |
| `ContainerName` | `"edict-claim-check"` | Container backing the claim-check escape hatch. |
| `BlobServiceClient` | `null` | Optional `BlobServiceClient`. A DI-registered singleton instance takes precedence so an `AddAzureClients()`-style power-user setup works without double-registration. |

## Cost vs. latency trade-off

`NumQueues` and `QueuePollingPeriod` ship above Orleans-conservative defaults so an interactive workload is not pinned to the latency floor out of the box. Both carry a direct cost trade-off on real Azure Storage:

| Knob | Edict default | Orleans default | Effect |
| --- | --- | --- | --- |
| `QueuePollingPeriod` | `10 ms` | `100 ms` | Per-event latency floor. Each tick costs a queue `GET` per consumer queue, billed and rate-limited under real Azure Storage. |
| `NumQueues` | `16` | `8` | Stream-provider fan-out — the consumer-parallelism ceiling. Each queue is polled independently at `QueuePollingPeriod`. |

The two compound: at the `10 ms` default poll period the per-queue GET cost runs roughly $3–6/day per silo per 8 queues. A cost-sensitive workload should lower `NumQueues` (4, 8) and raise `QueuePollingPeriod` (to hundreds of milliseconds or seconds) — trading interactive latency for a smaller storage bill. A high-throughput workload should leave both alone and raise `NumQueues` (32, 64) for more consumer parallelism on workloads that pay back the storage cost.

## Connection strings

Both extensions take Azure SDK clients, not raw connection strings. The clients can come from three places — whichever is set wins, in this order:

1. A DI-registered singleton `QueueServiceClient` / `BlobServiceClient` (the `AddAzureClients()` or `services.AddSingleton(client)` path).
2. The `QueueServiceClient` / `BlobServiceClient` property on the options object.
3. Neither — wiring throws `EdictWiringException` at `silo.AddEdictAzureStreams` / `silo.AddEdictAzureBlobClaimCheck`.

Local development uses Azurite via the `UseDevelopmentStorage=true` connection string. Production uses an Azure Storage account connection string or a `TokenCredential`-authenticated client. Edict does not surface either string directly; the consumer constructs the SDK client and the extension consumes it.

## See also

- [index.md](index.md) — the installation surface and fail-fast validation behaviour.
- [wiring/azure-streaming.md](../usage/wiring/azure-streaming.md) — the `Add*` call shape, client setup, and framework-author gotchas.
- [core.md](core.md) — the provider-agnostic `AddEdict()` knobs.
- ADRs — [0020 — Claim check for oversized events](../adr/0020-claim-check.md), [0023 — Config surface and installation](../adr/0023-config-surface-and-installation.md), [0042 — Azure package split](../adr/0042-azure-package-split.md).
