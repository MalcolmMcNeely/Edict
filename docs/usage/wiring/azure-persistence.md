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

A consumer reading projection or dead-letter rows from the client process must also register the read-side `IEdictTableRepository<T>` for each row type — see the dead-letter gotcha below.

## `EdictAzurePersistenceOptions`

| Property | Default | Purpose |
| --- | --- | --- |
| `GrainStateContainerName` | `"edict-state"` | Azure Blob container holding the Edict grain-state slot. Single-blob ETag atomicity covers the `[PersistentState("state", "edict-state")]` slot every framework grain base writes through. |
| `DeadLetterTableName` | `"edict-dead-letter"` | Backs the `IEdictTableRepository<EdictDeadLetterEntry>` registered by this extension. Does **not** drive where the projection writes — see the gotcha below. |
| `TableServiceClient` | `null` | Optional `TableServiceClient`. A DI-registered singleton instance takes precedence so an `AddAzureClients()`-style power-user setup works without double-registration. |
| `BlobServiceClient` | `null` | Optional `BlobServiceClient` for grain-state blobs. Same DI-precedence rule. |

The extension wires four Orleans pieces internally that the consumer does not configure directly:

- `AddAzureTableGrainStorage("PubSubStore")` — Orleans-internal pub/sub table.
- `AddAzureBlobGrainStorage("edict-state")` — the framework grain-state slot, on Blob (per ADR 0021).
- `UseAzureTableReminderService` — the reminder-tick substrate the outbox drain rides on.
- `IEdictTableStoreFactory` → `AzureTableWriteStoreFactory` — the per-table write seam projection builders use.

## Connection strings

Both clients (`TableServiceClient`, `BlobServiceClient`) can come from three places — whichever is set wins, in this order:

1. A DI-registered singleton client instance.
2. The matching `*Client` property on `EdictAzurePersistenceOptions`.
3. Neither — wiring throws `EdictWiringException` at `silo.AddEdictAzurePersistence`.

Local development uses Azurite via `UseDevelopmentStorage=true`. Production uses an Azure Storage account connection string or a `TokenCredential`-authenticated client. The two clients can point at the same account or split across accounts — table-storage limits (e.g. partition-throughput throttling) and blob-storage limits are independent, so a hot system can scale them separately.

## Gotchas

### `DeadLetterTableName` does not control where the projection writes

`EdictDeadLetterProjectionBuilder` writes every dead-letter row to a literal table named `"deadletter"` — the constant `EdictDeadLetterProjectionBuilder.DeadLetterPartition`, used as both the table name and the singleton partition key. The `DeadLetterTableName` option on this extension wires the operator-facing `IEdictTableRepository<EdictDeadLetterEntry>` to read from whatever you name there (default `"edict-dead-letter"`), so by default the repository looks at an empty table while the projection populates `"deadletter"`. A consumer reading dead-letter rows must register their own repository pointing at the literal:

```csharp
using Edict.Core.DeadLetter;

builder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(
    _ => new AzureTableRepository<EdictDeadLetterEntry>(
        tableServiceClient, EdictDeadLetterProjectionBuilder.DeadLetterPartition));
```

The Sample web project does exactly this. The framework option will stay until the projection is refactored to honour it.

### Azurite is not bit-for-bit Azure Table Storage

Azurite's table emulator is close enough that the conformance battery runs against it, but two differences bite:

- Azurite accepts table names Azure rejects. Azure Table names must match `^[A-Za-z][A-Za-z0-9]{2,62}$` — no hyphens, no underscores. The default `"edict-dead-letter"` works on Azurite and fails on real Azure; the literal projection table `"deadletter"` works on both. If you override `DeadLetterTableName`, keep it Azure-compliant.
- Azurite's per-property throttling and partition-server load shedding are weaker than real Azure. Throughput sweeps that pass against Azurite may surface real-Azure throttling that the local battery does not.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Outbox`, `Dead Letter`, `Table Projection Builder`, `Table Repository`.
- Concepts — [dead-letter.md](../concepts/dead-letter.md), [table-projections.md](../concepts/table-projections.md), [projection-builders.md](../concepts/projection-builders.md), [idempotency.md](../concepts/idempotency.md).
- Wiring — [azure-streaming.md](azure-streaming.md), [postgres.md](postgres.md).
- ADRs — [0021 — Grain state on blob substrate](../../adr/0021-grain-state-on-blob-substrate.md), [0023 — Config surface and installation](../../adr/0023-config-surface-and-installation.md), [0018 — Dead letter (forensic-only)](../../adr/0018-dead-letter-forensic-only.md), [0042 — Azure package split](../../adr/0042-azure-package-split.md).
