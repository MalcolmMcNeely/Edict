# Postgres wiring

The Postgres persistence side ships in `Edict.Postgres` and is wired through one `ISiloBuilder` extension, `AddEdictPostgresPersistence`. It registers `EdictPostgresGrainStorage` for the `edict-state` slot, the Postgres reminder service, the table write-store factory, the Postgres-backed claim-check store, and idempotently runs the embedded DDL bootstrap. Pair with `AddEdictKafkaStreams` or `AddEdictAzureStreams` for the wire side.

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

### Reading the audit log from a client

The silo captures the audit log into the Postgres WORM store when it is wired with `AddEdictPostgresPersistence` plus `silo.WithAudit()`. A separate process that only reads it — an Orleans client, a web host, an audit console — registers the read side with `AddEdictPostgresAuditReader`. It points at the same database and audit table names the silo uses, registers the Postgres audit stores, and resolves an `IEdictAuditRepository` over them. It needs no grain storage, reminders, or capture path; an Orleans `Serializer` (already present on a client host) is the only other requirement.

```csharp
using Edict.Postgres;

builder.Services.AddEdictPostgresAuditReader(o =>
    o.ConnectionString = builder.Configuration.GetConnectionString("appdb")!);
```

The page or service then injects `IEdictAuditRepository` and queries by entity, correlation, or principal, verifies a stored chain with `VerifyEntityChainAsync`, and fetches a captured body with `GetPayloadAsync`. To verify a chain already held in memory — for example a deliberately altered copy, since the WORM store refuses an in-place edit — call the pure `EdictAuditChain.Verify(records)`; it reports the first record whose hash or linkage fails.

## Configuration

`EdictPostgresPersistenceOptions` (the connection string, the grain-storage and table names, the schema-bootstrap toggle, and the connection-pool bounds), the connection-string format, and the pool-sizing math against Postgres `max_connections` are documented in [configuration/postgres.md](../../configuration/postgres.md).

## Tenant isolation on Postgres

When tenancy is on (`AddEdictTenant`), a tenant-scoped aggregate's grain key, stream key, and projection partition all fold the tenant through one composition chokepoint, so a tenant's state lands in its own key space and a read scoped to another tenant finds nothing by construction. Postgres adds a second line below the keying that Azure Table has no equivalent for:

- **Keying (primary).** The composed `{tenant}|{guid}` key is what physically separates one tenant's rows from another's in the shared tables. This is the primary control.
- **Row-Level Security (backstop).** `edict_grain_state` and every projection table carry a `FOR SELECT` row-security policy that confines a read to the ambient tenant. The framework establishes that tenant per transaction with `SET LOCAL` (via `set_config('edict.tenant', …, true)`), sourced from the authenticated origin's relay — *independent of the queried key*. So even a keying bug that asks the database for another tenant's rows comes back empty: the database checks the row's tenant prefix against the session tenant and denies the mismatch. Keying is primary; RLS is defence-in-depth.
- **Conformance.** A Postgres-only assertion proves the backstop denies a cross-tenant read independently of keying: the queried key is held identical while only the ambient tenant changes, and the cross-tenant read returns nothing.

The policy is permissive when no tenant is in the session, so a public aggregate, a single-tenant app, an operator read, and the schema bootstrap all see every row and pay nothing.

> **Operator requirement: the production connection must be a non-superuser, non-`BYPASSRLS` role.** Postgres bypasses Row-Level Security entirely for a superuser, and for the table owner unless `FORCE ROW LEVEL SECURITY` is set (the framework sets it). Edict does not create or own a database role — credentials are a deployment concern — so for the RLS backstop to be real, point `EdictPostgresPersistenceOptions.ConnectionString` at a role that is neither a superuser nor holds `BYPASSRLS`. Connecting as the default `postgres` superuser leaves only the keying and the isolation call filter as controls; the database wall is silently inert.

## Gotchas

### Edict ships its own grain-storage provider — do not swap for `AdoNetGrainStorage`

The extension registers `EdictPostgresGrainStorage` for the `edict-state` slot, not Orleans 10's shipped `AdoNetGrainStorage`. The shipped provider hard-codes the literal `"state"` as the row-key discriminator ([dotnet/orleans#9737](https://github.com/dotnet/orleans/issues/9737)), so every `Grain<T>` sharing a grain id — the command handler and any per-aggregate projection grain on the same `[RouteKey]` — collapses into one row and races on `ETag`. `EdictPostgresGrainStorage` keys on `(grain_type, grain_id, state_name, service_id)` instead so concept-level grains stay distinct. Orleans' shipped `AdoNetGrainStorage` is still wired for `PubSubStore` only — its grain type is Orleans-internal and no other grain type shares its key shape, so the collision does not bite there.

### One `NpgsqlDataSource` singleton serves every Postgres call-site

The extension builds one `NpgsqlDataSource` at silo wiring time and registers it as a singleton. Grain storage, the projection store, claim-check store, and the DDL bootstrap all run against this one data source so the connection pool is owned in a single place. Per ADR 0035, this matters operationally: `NpgsqlDataSource` exposes `db.client.connection.*` OpenTelemetry instruments only when one process owns one data source. The framework registers it via factory (`AddSingleton<NpgsqlDataSource>(_ => dataSource)`) so the container disposes it on teardown — `AddSingleton(instance)` would skip `IDisposable` tracking and leak the pool.

Orleans' shipped `AdoNetGrainStorage` (used for `PubSubStore`) owns its own connection-string-keyed Npgsql pool. That's two pools per silo: Edict's tuned one plus Orleans' default-sized one for `PubSubStore` and reminders. The Orleans pool is not load-bearing for command throughput and does not need to match Edict's tuning.

## See also

- `CONTEXT.md` — [Language](../../../CONTEXT.md#language): `Outbox`, `Dead Letter`, `List Projection Builder`, `Projection Reader`, `Claim Check`.
- Concepts — [dead-letter.md](../concepts/dead-letter.md), [projections.md](../concepts/projections.md), [claim-check.md](../concepts/claim-check.md), [idempotency.md](../concepts/idempotency.md), [audit-log.md](../concepts/audit-log.md).
- Configuration — [postgres.md](../../configuration/postgres.md) — the options table, connection-string format, and pool-sizing math.
- Wiring — [kafka.md](kafka.md), [azure-streaming.md](azure-streaming.md), [azure-persistence.md](azure-persistence.md).
- ADRs — [0029 — Postgres persistence schema](../../adr/0029-postgres-persistence-schema.md), [0035 — Npgsql DataSource singleton](../../adr/0035-npgsql-datasource-singleton.md), [0023 — Config surface and installation](../../adr/0023-config-surface-and-installation.md), [0067 — Tenant isolation enforcement and storage](../../adr/0067-tenant-isolation-enforcement.md).
