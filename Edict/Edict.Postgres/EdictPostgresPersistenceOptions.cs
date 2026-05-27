namespace Edict.Postgres;

/// <summary>
/// Tuning knobs for the Postgres persistence provider. Brand-prefixed because
/// the consumer types it. Defaults make the simple path zero-config; every
/// knob lands as a property so an operator has one obvious place to tune.
/// </summary>
public sealed class EdictPostgresPersistenceOptions
{
    /// <summary>
    /// Npgsql connection string. The consumer-required knob — Postgres has
    /// no Azurite-style local default, so there's no defensible literal here.
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Postgres invariant name used by Orleans' ADO.NET provider. Pinned to
    /// Npgsql; exposed in case the consumer ships a Postgres-compatible
    /// alternative driver.
    /// </summary>
    public string Invariant { get; set; } = "Npgsql";

    /// <summary>
    /// Orleans grain-storage provider name. Defaults to <c>edict-state</c> so
    /// the <c>[PersistentState("state", "edict-state")]</c> attribute on the
    /// framework's grain bases resolves without consumer wiring — same shape as
    /// the Azure provider.
    /// </summary>
    public string GrainStorageProviderName { get; set; } = "edict-state";

    /// <summary>
    /// Table name backing the forensic dead-letter projection. The framework's
    /// EdictDeadLetterProjectionBuilder writes the consumer-typed
    /// <see cref="Edict.Contracts.DeadLetter.EdictDeadLetterEntry"/> through
    /// the table-store factory; the Postgres impl materialises the structured
    /// columns alongside the MessagePack payload.
    /// </summary>
    public string DeadLetterTableName { get; set; } = "edict_dead_letter";

    /// <summary>
    /// Table backing the append-only claim-check escape hatch (ADR-0020).
    /// Postgres has no per-row cap (TOAST handles large payloads via lz4
    /// compression) — Edict still uses claim-check on the Postgres pairing
    /// because the wire substrate (Kafka or AQS) has its own per-message limit.
    /// </summary>
    public string ClaimCheckTableName { get; set; } = "edict_claim_check";

    /// <summary>
    /// Run the embedded Orleans + Edict DDL scripts at silo wiring time. The
    /// scripts are idempotent (Edict tables use <c>CREATE TABLE IF NOT EXISTS</c>;
    /// Orleans tables are skipped if their canonical table already exists), so
    /// the simple path needs no migration tooling. Set to <c>false</c> when a
    /// deployment pipeline already manages the schema.
    /// </summary>
    public bool BootstrapSchema { get; set; } = true;
}
