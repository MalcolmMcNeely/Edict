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

## `EdictPostgresPersistenceOptions`

| Property | Default | Purpose |
| --- | --- | --- |
| `ConnectionString` | `""` | **Required.** Npgsql connection string. No default — Postgres has no Azurite-style local emulator. Empty value throws `EdictWiringException` at wiring time. |
| `Invariant` | `"Npgsql"` | ADO.NET invariant name used by Orleans' shipped ADO.NET providers (`PubSubStore`, reminders). Pinned to Npgsql; exposed for a Postgres-compatible driver alternative. |
| `GrainStorageProviderName` | `"edict-state"` | Keyed name under which `EdictPostgresGrainStorage` is registered. Defaults match the `[PersistentState("state", "edict-state")]` attribute on framework grain bases, so consumer wiring is zero-config. |
| `DeadLetterTableName` | `"edict_dead_letter"` | Backs the `IEdictTableRepository<EdictDeadLetterEntry>` registered by this extension. Does **not** drive where the projection writes — see the gotcha below. |
| `ClaimCheckTableName` | `"edict_claim_check"` | Table backing the append-only claim-check escape hatch. Postgres has no per-row cap (TOAST handles large payloads), but Edict still uses claim-check on the Postgres pairing because the wire substrate (Kafka or AQS) has its own per-message limit. |
| `BootstrapSchema` | `true` | Run the embedded Orleans + Edict DDL at silo wiring time. Idempotent — Edict tables use `CREATE TABLE IF NOT EXISTS`; Orleans tables are skipped if their canonical table already exists. Set to `false` when a deployment pipeline manages the schema. |
| `MaxPoolSize` | `200` | Upper bound on connections held by the shared `NpgsqlDataSource`. Wins over any `Maximum Pool Size` keyword in the connection string. See the `max_connections` gotcha below. |
| `MinPoolSize` | `10` | Minimum number of connections the shared `NpgsqlDataSource` pre-creates at startup. Absorbs the slow `create_time` tail observed at `N = 64` (p99 1.31 s per new pooled connection) so first-burst traffic doesn't pay establishment latency. Wins over any `Minimum Pool Size` keyword in the connection string. |

## Connection strings

`ConnectionString` is a raw Npgsql connection string: `Host=…;Port=…;Database=…;Username=…;Password=…`. Local development pulls it from Aspire's `appdb` resource (a `Aspire.Hosting.Postgres` container). Production passes it from configuration. `MaxPoolSize` and `MinPoolSize` on the options surface take precedence over `Maximum Pool Size` / `Minimum Pool Size` keywords in the string — the options surface is the one obvious place to tune; conflicting keywords stay as no-ops.

## Gotchas

### Edict ships its own grain-storage provider — do not swap for `AdoNetGrainStorage`

The extension registers `EdictPostgresGrainStorage` for the `edict-state` slot, not Orleans 10's shipped `AdoNetGrainStorage`. The shipped provider hard-codes the literal `"state"` as the row-key discriminator ([dotnet/orleans#9737](https://github.com/dotnet/orleans/issues/9737)), so every `Grain<T>` sharing a grain id — the command handler and any per-aggregate projection grain on the same `[RouteKey]` — collapses into one row and races on `ETag`. `EdictPostgresGrainStorage` keys on `(grain_type, grain_id, state_name, service_id)` instead so concept-level grains stay distinct. Orleans' shipped `AdoNetGrainStorage` is still wired for `PubSubStore` only — its grain type is Orleans-internal and no other grain type shares its key shape, so the collision does not bite there.

### One `NpgsqlDataSource` singleton serves every Postgres call-site

The extension builds one `NpgsqlDataSource` at silo wiring time and registers it as a singleton. Grain storage, table repositories, claim-check store, and the DDL bootstrap all run against this one data source so the connection pool is owned in a single place. Per ADR 0035, this matters operationally: `NpgsqlDataSource` exposes `db.client.connection.*` OpenTelemetry instruments only when one process owns one data source. The framework registers it via factory (`AddSingleton<NpgsqlDataSource>(_ => dataSource)`) so the container disposes it on teardown — `AddSingleton(instance)` would skip `IDisposable` tracking and leak the pool.

Orleans' shipped `AdoNetGrainStorage` (used for `PubSubStore`) owns its own connection-string-keyed Npgsql pool. That's two pools per silo: Edict's tuned one plus Orleans' default-sized one for `PubSubStore` and reminders. The Orleans pool is not load-bearing for command throughput and does not need to match Edict's tuning.

### `MaxPoolSize` × silo count must fit under Postgres `max_connections`

Each silo's `MaxPoolSize` is a multiplier against Postgres `max_connections`. The default `MaxPoolSize = 200` gives a single silo 2× headroom against the published `N = 256` closed-loop sweep point and absorbs the projection/idempotency grain-turn demand the headline EPS number does not include. A multi-silo deployment must size Postgres accordingly:

| Silos | Edict pools | Ambient (admin, monitoring) | Client process | Suggested `max_connections` |
| --- | --- | --- | --- | --- |
| 1 | 200 | 100 | 100 | 400+ |
| 2 | 400 | 100 | 100 | 600+ |
| 5 | 1 000 | 100 | 100 | 1 200+ |

Postgres 16's default `max_connections = 100` will not survive any non-trivial throughput. The Kafka+Postgres throughput substrate raises it to `1024` for two-silo bench runs. Either raise the Postgres ceiling or lower `MaxPoolSize` to fit; the throughput floor at a lower `MaxPoolSize` is real but acceptable for development.

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

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Outbox`, `Dead Letter`, `Table Projection Builder`, `Table Repository`, `Claim Check`.
- Concepts — [dead-letter.md](../concepts/dead-letter.md), [table-projections.md](../concepts/table-projections.md), [claim-check.md](../concepts/claim-check.md), [idempotency.md](../concepts/idempotency.md).
- Wiring — [kafka.md](kafka.md), [azure-streaming.md](azure-streaming.md), [azure-persistence.md](azure-persistence.md).
- ADRs — [0029 — Postgres persistence schema](../../adr/0029-postgres-persistence-schema.md), [0035 — Npgsql DataSource singleton](../../adr/0035-npgsql-datasource-singleton.md), [0023 — Config surface and installation](../../adr/0023-config-surface-and-installation.md).
