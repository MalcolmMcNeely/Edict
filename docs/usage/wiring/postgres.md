# Postgres wiring

The Postgres persistence side ships in `Edict.Postgres` and is wired through one `ISiloBuilder` extension, `AddEdictPostgresPersistence`. It registers `EdictPostgresGrainStorage` for the `edict-state` slot, the Postgres reminder service, the table write-store factory, the dead-letter table repository, the Postgres-backed claim-check store, and idempotently runs the embedded DDL bootstrap. Pair with `AddEdictKafkaStreams` or `AddEdictAzureStreams` for the wire side.

## Silo setup

```csharp
using Edict.Core;
using Edict.Core.Serialization;
using Edict.Postgres;

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

        // Pair with a streaming extension (AddEdictKafkaStreams or AddEdictAzureStreams).

        silo.AddEdictPostgresPersistence(o =>
        {
            o.ConnectionString = context.Configuration.GetConnectionString("appdb")
                ?? throw new InvalidOperationException("Postgres connection string 'appdb' missing.");
        });
    });
```

## Client setup

The client process does not call `AddEdictPostgresPersistence` — persistence is silo-side. A client process that reads projection or dead-letter rows holds its own `NpgsqlDataSource` (default-pooled — the read path is not throughput-sensitive) and registers a `PostgresTableRepository<T>` per row type.

```csharp
using Edict.Core;
using Edict.Core.Serialization;
using Edict.Postgres.TableStorage;

using Npgsql;

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

builder.Services.AddSingleton(
    new NpgsqlDataSourceBuilder(builder.Configuration.GetConnectionString("appdb")!).Build());

builder.Services.AddEdict();
```

## Configuration

`EdictPostgresPersistenceOptions` (the connection string, the grain-storage and table names, the schema-bootstrap toggle, and the connection-pool bounds), the connection-string format, and the pool-sizing math against Postgres `max_connections` are documented in [configuration/postgres.md](../../configuration/postgres.md).

## Gotchas

### Edict ships its own grain-storage provider — do not swap for `AdoNetGrainStorage`

The extension registers `EdictPostgresGrainStorage` for the `edict-state` slot, not Orleans 10's shipped `AdoNetGrainStorage`. The shipped provider hard-codes the literal `"state"` as the row-key discriminator ([dotnet/orleans#9737](https://github.com/dotnet/orleans/issues/9737)), so every `Grain<T>` sharing a grain id — the command handler and any per-aggregate projection grain on the same `[RouteKey]` — collapses into one row and races on `ETag`. `EdictPostgresGrainStorage` keys on `(grain_type, grain_id, state_name, service_id)` instead so concept-level grains stay distinct. Orleans' shipped `AdoNetGrainStorage` is still wired for `PubSubStore` only — its grain type is Orleans-internal and no other grain type shares its key shape, so the collision does not bite there.

### One `NpgsqlDataSource` singleton serves every Postgres call-site

The extension builds one `NpgsqlDataSource` at silo wiring time and registers it as a singleton. Grain storage, table repositories, claim-check store, and the DDL bootstrap all run against this one data source so the connection pool is owned in a single place. Per ADR 0035, this matters operationally: `NpgsqlDataSource` exposes `db.client.connection.*` OpenTelemetry instruments only when one process owns one data source. The framework registers it via factory (`AddSingleton<NpgsqlDataSource>(_ => dataSource)`) so the container disposes it on teardown — `AddSingleton(instance)` would skip `IDisposable` tracking and leak the pool.

Orleans' shipped `AdoNetGrainStorage` (used for `PubSubStore`) owns its own connection-string-keyed Npgsql pool. That's two pools per silo: Edict's tuned one plus Orleans' default-sized one for `PubSubStore` and reminders. The Orleans pool is not load-bearing for command throughput and does not need to match Edict's tuning.

### `DeadLetterTableName` does not control where the projection writes

The auto-wired projection writes every dead-letter row to a literal table named `"deadletter"` — the constant `EdictDeadLetterTable.Name`. The `DeadLetterTableName` option on this extension wires the operator-facing `IEdictTableRepository<EdictDeadLetterEntry>` to read from whatever you name there (default `"edict_dead_letter"`), so by default the repository looks at an empty table while the projection populates `"deadletter"`. A consumer reading dead-letter rows must register their own repository pointing at the literal:

```csharp
using Edict.Core.DeadLetter;
using Edict.Postgres.TableStorage;

builder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(serviceProvider =>
    new PostgresTableRepository<EdictDeadLetterEntry>(
        serviceProvider.GetRequiredService<NpgsqlDataSource>(),
        EdictDeadLetterTable.Name,
        serviceProvider.GetRequiredService<Serializer>()));
```

The Sample web project does exactly this. The framework option will stay until the projection is refactored to honour it.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Outbox`, `Dead Letter`, `List Projection Builder`, `Table Repository`, `Claim Check`.
- Concepts — [dead-letter.md](../concepts/dead-letter.md), [table-projections.md](../concepts/table-projections.md), [claim-check.md](../concepts/claim-check.md), [idempotency.md](../concepts/idempotency.md).
- Configuration — [postgres.md](../../configuration/postgres.md) — the options table, connection-string format, and pool-sizing math.
- Wiring — [kafka.md](kafka.md), [azure-streaming.md](azure-streaming.md), [azure-persistence.md](azure-persistence.md).
- ADRs — [0029 — Postgres persistence schema](../../adr/0029-postgres-persistence-schema.md), [0035 — Npgsql DataSource singleton](../../adr/0035-npgsql-datasource-singleton.md), [0023 — Config surface and installation](../../adr/0023-config-surface-and-installation.md).
