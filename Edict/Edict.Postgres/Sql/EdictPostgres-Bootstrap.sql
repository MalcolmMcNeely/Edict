-- Edict.Postgres framework-owned schema. Additive only — no column rename,
-- no NOT NULL on existing tables without defaults. Runs after the Orleans
-- AdoNet scripts at silo startup; idempotent via CREATE TABLE IF NOT EXISTS.

-- Edict-owned grain-state table. Edict.Postgres ships its own grain-storage
-- provider (EdictPostgresGrainStorage) instead of Orleans 10's
-- AdoNetGrainStorage because the latter hard-codes the literal "state" as
-- the row-key discriminator instead of the grain type, collapsing every
-- Grain<T> that shares a grain id into one row that races on ETag
-- (https://github.com/dotnet/orleans/issues/9737). Including grain_type in
-- the composite PK is what keeps per-aggregate projections, singleton
-- projections, and command handlers distinct when they all key on the same
-- RouteKey Guid.
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
    id          TEXT PRIMARY KEY,
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

-- ALCOA++ audit log: the per-aggregate tamper-evident chain. Structured columns
-- are queryable by entity/correlation/principal; the full record (including the
-- chain hashes) round-trips through the contract serializer in `record`. The hash
-- is computed over a frozen canonical form, not these columns, so a column read
-- is for querying only. Append-only: a BEFORE UPDATE/DELETE trigger raises, so
-- the table is tamper-*prevention* (not merely evidence) even for the table owner
-- and a superuser, who bypass REVOKE.
CREATE TABLE IF NOT EXISTS edict_audit_record
(
    record_id      UUID PRIMARY KEY,
    entity_type    TEXT        NOT NULL,
    entity_key     TEXT        NOT NULL,
    sequence       BIGINT      NOT NULL,
    correlation_id UUID        NOT NULL,
    principal      TEXT,
    kind           TEXT        NOT NULL,
    outcome        TEXT,
    occurred_at    TIMESTAMPTZ NOT NULL,
    record         BYTEA       NOT NULL,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_edict_audit_entity
    ON edict_audit_record (entity_type, entity_key, sequence);

CREATE INDEX IF NOT EXISTS ix_edict_audit_correlation
    ON edict_audit_record (correlation_id);

CREATE INDEX IF NOT EXISTS ix_edict_audit_principal
    ON edict_audit_record (principal);

CREATE OR REPLACE FUNCTION edict_audit_record_block_mutation()
    RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'edict_audit_record is append-only (WORM): % is not permitted', TG_OP
        USING ERRCODE = 'insufficient_privilege';
END;
$$ LANGUAGE plpgsql;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'edict_audit_record_worm') THEN
        CREATE TRIGGER edict_audit_record_worm
            BEFORE UPDATE OR DELETE ON edict_audit_record
            FOR EACH ROW EXECUTE FUNCTION edict_audit_record_block_mutation();
    END IF;
END $$;

-- ALCOA++ audit log: the captured message bodies, keyed by audit record id. The
-- chain link (edict_audit_record) holds only a hash and this reference, never the
-- body, so the tamper-evident chain stays personal-data-free and the body slice can
-- be crypto-shredded later without breaking a hash. Distinct from edict_claim_check:
-- that is event-only, EventId-keyed, and lifecycle-reaped; this holds every captured
-- command and event body and is infinite-retention. Append-only by the same trigger
-- discipline as the chain table: a BEFORE UPDATE/DELETE trigger raises, so the body
-- is tamper-*prevention* even for the table owner and a superuser, who bypass REVOKE.
CREATE TABLE IF NOT EXISTS edict_audit_payload
(
    record_id   UUID PRIMARY KEY,
    payload     BYTEA       NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE OR REPLACE FUNCTION edict_audit_payload_block_mutation()
    RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'edict_audit_payload is append-only (WORM): % is not permitted', TG_OP
        USING ERRCODE = 'insufficient_privilege';
END;
$$ LANGUAGE plpgsql;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'edict_audit_payload_worm') THEN
        CREATE TRIGGER edict_audit_payload_worm
            BEFORE UPDATE OR DELETE ON edict_audit_payload
            FOR EACH ROW EXECUTE FUNCTION edict_audit_payload_block_mutation();
    END IF;
END $$;
