# Azure persistence wiring

The Azure persistence side ships in `Edict.Azure.Persistence` and is wired through one `ISiloBuilder` extension, `AddEdictAzurePersistence`. It chains the Orleans Azure grain-storage, reminder, and table-storage primitives plus the Edict provider seams (dead-letter table repository, table write-store factory) into a single `Action` lambda.

## Silo setup

```csharp
using Azure.Data.Tables;
using Azure.Storage.Blobs;

using Edict.Azure.Persistence;
using Edict.Core;
using Edict.Core.Serialization;

using Orleans.Serialization;

Host.CreateDefaultBuilder(args)
    .UseOrleans((context, silo) =>
    {
        silo.UseLocalhostClustering();
        silo.Services.AddSerializer(ser =>
        {
            ser.AddAssembly(typeof(OrderCommandHandler).Assembly);
            ser.AddEdictContractSerializer();
        });

        silo.AddEdict();

        // Pair with a streaming extension (AddEdictAzureStreams or AddEdictKafkaStreams).

        silo.AddEdictAzurePersistence(o =>
        {
            o.TableServiceClient = new TableServiceClient(context.Configuration.GetConnectionString("tables"));
            o.BlobServiceClient  = new BlobServiceClient(context.Configuration.GetConnectionString("blobs"));
        });
    });
```

## Client setup

The client process does not call `AddEdictAzurePersistence` — persistence is a silo-side decision. The client registers the consumer's command-handler interface assembly so grain calls can serialise.

```csharp
using Edict.Core;
using Edict.Core.Serialization;

using Orleans.Serialization;

builder.UseOrleansClient(client =>
{
    client.UseLocalhostClustering();
    client.Services.AddSerializer(ser =>
    {
        ser.AddAssembly(typeof(IOrderCommandHandler).Assembly);
        ser.AddEdictContractSerializer();
    });
});

builder.Services.AddEdict();
```

A consumer reading projection or dead-letter rows from the client process needs no read-side storage wiring: `AddEdict()` registers `IEdictProjectionReader<TRow>` (open-generic) and the grain-backed `IEdictDeadLetterRepository`, both of which route reads through the projection grain.

## Configuration

`EdictAzurePersistenceOptions` (the grain-state container and the optional service-client overrides) and the connection-string precedence rules are documented in [configuration/azure-persistence.md](../../configuration/azure-persistence.md).

The extension wires four Orleans pieces internally that the consumer does not configure directly:

- `AddAzureTableGrainStorage("PubSubStore")` — Orleans-internal pub/sub table.
- `AddAzureBlobGrainStorage("edict-state")` — the framework grain-state slot, on Blob (per ADR 0021).
- `UseAzureTableReminderService` — the reminder-tick substrate the outbox drain rides on.
- `IEdictTableStoreFactory` → `AzureTableWriteStoreFactory` — the per-table write seam projection builders use.

## The audit log on Azure

`AddEdictAzurePersistence` provisions the audit stores unconditionally — an Azure **Table** for the chain (`AuditTableName`) and an Immutable **Blob** container for the captured bodies (`AuditPayloadContainerName`) — but they stay dormant until `silo.WithAudit()` turns capture on. Arm it the same way as any substrate:

```csharp
silo.AddEdictAzurePersistence(o => { /* clients */ });
silo.Services.AddEdictAudit(serviceProvider => /* resolve the origin principal */);
silo.WithAudit();
```

`WithAudit()` registers `IEdictAuditRepository` over the Azure stores, so the read surface is available **in the silo's own services**. A separate process that only reads the audit log — an Orleans client, a web host — registers the stores against the same Table and Blob account with `AddEdictAzureAuditReader`, the Azure counterpart to `AddEdictPostgresAuditReader`:

```csharp
builder.Services.AddEdictAzureAuditReader(o =>
{
    o.TableServiceClient = new TableServiceClient(tableConnectionString);
    o.BlobServiceClient  = new BlobServiceClient(blobConnectionString);
    // AuditTableName and AuditPayloadContainerName must match the capturing silo;
    // both default to the same names the silo uses.
});
```

It wraps the already-provisioned Table and container (no grain storage, reminders, or capture path) and delegates to the provider-agnostic `AddEdictAuditReader`, so the `IEdictAuditRepository` surface it exposes is identical to Postgres and the audit page reads the same across substrates. An Orleans `Serializer` must be in the container to type a captured body back; an Orleans client host already registers one.

The honest consequence to carry to consumers: the Azure-Table chain is tamper-**evidence** (the per-aggregate hash chain, re-walked by `VerifyEntityChainAsync`) without infrastructure tamper-**prevention** until the deferred blob-sealing slice. Postgres has both; Azure has evidence only. See [concepts/audit-log.md](../concepts/audit-log.md#per-substrate-tamper-prevention-vs-evidence) for the full prevention-versus-evidence distinction.

## Gotchas

### Azurite is not bit-for-bit Azure Table Storage

Azurite's table emulator is close enough that the conformance battery runs against it, but two differences bite:

- Azurite accepts table names Azure rejects. Azure Table names must match `^[A-Za-z][A-Za-z0-9]{2,62}$` — no hyphens, no underscores. The dead-letter projection's literal table `"deadletter"` is Azure-compliant; any projection `TableName` you author must be too.
- Azurite's per-property throttling and partition-server load shedding are weaker than real Azure. Throughput sweeps that pass against Azurite may surface real-Azure throttling that the local battery does not.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Outbox`, `Dead Letter`, `List Projection Builder`, `Projection Reader`.
- Concepts — [dead-letter.md](../concepts/dead-letter.md), [projections.md](../concepts/projections.md), [idempotency.md](../concepts/idempotency.md), [audit-log.md](../concepts/audit-log.md).
- Configuration — [azure-persistence.md](../../configuration/azure-persistence.md) — the options table and connection-string rules.
- Wiring — [azure-streaming.md](azure-streaming.md), [postgres.md](postgres.md).
- ADRs — [0021 — Grain state on blob substrate](../../adr/0021-grain-state-on-blob-substrate.md), [0023 — Config surface and installation](../../adr/0023-config-surface-and-installation.md), [0018 — Dead letter (forensic-only)](../../adr/0018-dead-letter-forensic-only.md), [0042 — Azure package split](../../adr/0042-azure-package-split.md).
