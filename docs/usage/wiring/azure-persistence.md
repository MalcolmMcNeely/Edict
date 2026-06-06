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

## Gotchas

### Azurite is not bit-for-bit Azure Table Storage

Azurite's table emulator is close enough that the conformance battery runs against it, but two differences bite:

- Azurite accepts table names Azure rejects. Azure Table names must match `^[A-Za-z][A-Za-z0-9]{2,62}$` — no hyphens, no underscores. The dead-letter projection's literal table `"deadletter"` is Azure-compliant; any projection `TableName` you author must be too.
- Azurite's per-property throttling and partition-server load shedding are weaker than real Azure. Throughput sweeps that pass against Azurite may surface real-Azure throttling that the local battery does not.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Outbox`, `Dead Letter`, `List Projection Builder`, `Projection Reader`.
- Concepts — [dead-letter.md](../concepts/dead-letter.md), [projections.md](../concepts/projections.md), [idempotency.md](../concepts/idempotency.md).
- Configuration — [azure-persistence.md](../../configuration/azure-persistence.md) — the options table and connection-string rules.
- Wiring — [azure-streaming.md](azure-streaming.md), [postgres.md](postgres.md).
- ADRs — [0021 — Grain state on blob substrate](../../adr/0021-grain-state-on-blob-substrate.md), [0023 — Config surface and installation](../../adr/0023-config-surface-and-installation.md), [0018 — Dead letter (forensic-only)](../../adr/0018-dead-letter-forensic-only.md), [0042 — Azure package split](../../adr/0042-azure-package-split.md).
