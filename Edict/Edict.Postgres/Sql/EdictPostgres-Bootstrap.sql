-- Edict.Postgres framework-owned schema. Additive only — no column rename,
-- no NOT NULL on existing tables without defaults. Runs after the Orleans
-- AdoNet scripts at silo startup; idempotent via CREATE TABLE IF NOT EXISTS.

-- Edict-owned grain-state table. Edict.Postgres ships its own grain-storage
-- provider (EdictPostgresGrainStorage) instead of Orleans 10's
-- AdoNetGrainStorage because the latter uses the literal "state" string as
-- the row discriminator instead of the grain type, collapsing every
-- Grain<T>-derived grain that shares an id into one row (dotnet/orleans
-- issue #9737). Including grain_type in the composite PK is what
-- distinguishes per-aggregate projections, singleton projections, and
-- command handlers that all key on the same RouteKey Guid.
CREATE TABLE IF NOT EXISTS edict_grain_state
(
    grain_type   TEXT        NOT NULL,
    grain_id     TEXT        NOT NULL,
    state_name   TEXT        NOT NULL,
    service_id   TEXT        NOT NULL,
    payload      BYTEA,
    version      INT         NOT NULL,
    modified_on  TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (grain_type, grain_id, state_name, service_id)
);

CREATE TABLE IF NOT EXISTS edict_claim_check
(
    id          UUID PRIMARY KEY,
    payload     BYTEA       NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL
);

-- Forensic dead-letter projection. Structured columns are SQL-queryable by
-- operators for error-pattern grouping; the full MessagePack payload lives in
-- `payload` so IEdictTableRepository<EdictDeadLetterEntry>.GetAsync round-trips
-- the row through the contract serializer.
CREATE TABLE IF NOT EXISTS deadletter
(
    partition_key TEXT        NOT NULL,
    row_key       TEXT        NOT NULL,
    entry_id      UUID,
    kind          TEXT,
    attempt_count INT,
    source_event_type   TEXT,
    reason        TEXT,
    deadlettered_at     TIMESTAMPTZ,
    payload       BYTEA       NOT NULL,
    etag          TEXT        NOT NULL,
    PRIMARY KEY (partition_key, row_key)
);

CREATE INDEX IF NOT EXISTS ix_deadletter_kind
    ON deadletter (kind);

CREATE INDEX IF NOT EXISTS ix_deadletter_source_event_type
    ON deadletter (source_event_type);
