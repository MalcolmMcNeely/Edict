# Postgres configuration

`EdictPostgresPersistenceOptions` backs `AddEdictPostgresPersistence`, the single extension that registers `EdictPostgresGrainStorage`, the Postgres reminder service, the table write-store factory, the dead-letter repository, the Postgres-backed claim-check store, and runs the embedded DDL bootstrap. For the `Add*` call shape, the client-side setup, and the framework-author gotchas (the custom grain-storage provider, the single-`NpgsqlDataSource` ownership rule, the `max_connections` sizing math, and the dead-letter projection-table caveat), see [wiring/postgres.md](../usage/wiring/postgres.md).

## `EdictPostgresPersistenceOptions`

| Property | Default | Purpose |
| --- | --- | --- |
| `ConnectionString` | `""` | **Required.** Npgsql connection string. No default — Postgres has no Azurite-style local emulator. An empty value throws `EdictWiringException` at wiring time. |
| `Invariant` | `"Npgsql"` | ADO.NET invariant name used by Orleans' shipped ADO.NET providers (`PubSubStore`, reminders). Pinned to Npgsql; exposed for a Postgres-compatible driver alternative. |
| `GrainStorageProviderName` | `"edict-state"` | Keyed name under which `EdictPostgresGrainStorage` is registered. Defaults match the `[PersistentState("state", "edict-state")]` attribute on framework grain bases, so consumer wiring is zero-config. |
| `ClaimCheckTableName` | `"edict_claim_check"` | Table backing the append-only claim-check escape hatch. Postgres has no per-row cap (TOAST handles large payloads), but Edict still uses claim-check on the Postgres pairing because the wire substrate (Kafka or AQS) has its own per-message limit. |
| `AuditTableName` | `"edict_audit_record"` | Table backing the append-only ALCOA++ audit chain. WORM-enforced by a `BEFORE UPDATE/DELETE` trigger in the bootstrap DDL, so it is tamper prevention as well as evidence. Override only when a deployment pipeline manages a differently-named audit table. |
| `AuditPayloadTableName` | `"edict_audit_payload"` | Table backing the append-only store of captured audit message bodies, keyed by audit record id and distinct from the claim-check table. WORM-enforced by a `BEFORE UPDATE/DELETE` trigger. The audit chain holds only a hash and a reference into this table, never the body, so the chain stays personal-data-free. Override only when a deployment pipeline manages a differently-named payload table. |
| `BootstrapSchema` | `true` | Run the embedded Orleans + Edict DDL at silo wiring time. Idempotent — Edict tables use `CREATE TABLE IF NOT EXISTS`; Orleans tables are skipped if their canonical table already exists. Set to `false` when a deployment pipeline manages the schema. |
| `MaxPoolSize` | `200` | Upper bound on connections held by the shared `NpgsqlDataSource`. Wins over any `Maximum Pool Size` keyword in `ConnectionString`. See the `max_connections` sizing math below. |
| `MinPoolSize` | `10` | Minimum number of connections the shared `NpgsqlDataSource` pre-creates at startup. Absorbs the slow `create_time` tail observed at `N = 64` (p99 1.31 s per new pooled connection) so first-burst traffic doesn't pay establishment latency. Wins over any `Minimum Pool Size` keyword in `ConnectionString`. |
| `StorageRetryCount` | `3` | Total attempts (one try plus retries) each grain-storage seam makes against a transient `NpgsqlException` before surfacing `EdictPostgresStorageException`. The Azure pairing gets transient-fault resilience free from its SDK; this provider rolls its own grain storage and gets none, so the retry is applied explicitly. Retry is gated on `NpgsqlException.IsTransient`; a non-transient fault surfaces on the first attempt, and `InconsistentStateException` propagates unretried as the concurrency signal. See the [retry counter](../operations/observability.md#postgres-grain-storage-transient-retry). |
| `StorageRetryBaseDelay` | `50 ms` | Base delay for the grain-storage retry backoff, applied exponentially with no jitter (50 ms, then 100 ms). Retrying a write is safe even when the first attempt may already have applied: the version check returns no row on a no-op, so a double-apply degrades to an `InconsistentStateException` that Orleans resolves by deactivating and re-reading. |

## Connection strings

`ConnectionString` is a raw Npgsql connection string: `Host=…;Port=…;Database=…;Username=…;Password=…`. Local development pulls it from Aspire's `appdb` resource (a `Aspire.Hosting.Postgres` container). Production passes it from configuration. `MaxPoolSize` and `MinPoolSize` on the options surface take precedence over the `Maximum Pool Size` / `Minimum Pool Size` keywords in the string — the options surface is the one obvious place to tune; conflicting keywords stay as no-ops.

## Pool sizing

`MaxPoolSize` defaults to `200`, giving a single silo 2× headroom against the published `N = 256` closed-loop sweep point and absorbing the projection/idempotency grain-turn demand the headline EPS number does not include. Each silo's `MaxPoolSize` is a multiplier against Postgres `max_connections`, so a multi-silo deployment must size Postgres accordingly:

| Silos | Edict pools | Ambient (admin, monitoring) | Client process | Suggested `max_connections` |
| --- | --- | --- | --- | --- |
| 1 | 200 | 100 | 100 | 400+ |
| 2 | 400 | 100 | 100 | 600+ |
| 5 | 1 000 | 100 | 100 | 1 200+ |

Postgres 16's default `max_connections = 100` will not survive any non-trivial throughput. Either raise the Postgres ceiling or lower `MaxPoolSize` to fit; the throughput floor at a lower `MaxPoolSize` is real but acceptable for development.

## See also

- [index.md](index.md) — the installation surface and fail-fast validation behaviour.
- [wiring/postgres.md](../usage/wiring/postgres.md) — the `Add*` call shape, client setup, and the grain-storage / data-source / `max_connections` / dead-letter gotchas.
- [core.md](core.md) — the provider-agnostic `AddEdict()` knobs.
- ADRs — [0029 — Postgres persistence schema](../adr/0029-postgres-persistence-schema.md), [0035 — Npgsql DataSource singleton](../adr/0035-npgsql-datasource-singleton.md), [0023 — Config surface and installation](../adr/0023-config-surface-and-installation.md).
