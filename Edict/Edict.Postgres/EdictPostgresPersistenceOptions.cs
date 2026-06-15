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
    /// Table backing the append-only claim-check escape hatch.
    /// Postgres has no per-row cap (TOAST handles large payloads via lz4
    /// compression) — Edict still uses claim-check on the Postgres pairing
    /// because the wire substrate (Kafka or AQS) has its own per-message limit.
    /// </summary>
    public string ClaimCheckTableName { get; set; } = "edict_claim_check";

    /// <summary>
    /// Table backing the append-only ALCOA++ audit chain. WORM-enforced by a
    /// BEFORE UPDATE/DELETE trigger in the bootstrap DDL, so it is tamper
    /// prevention as well as evidence. Defaults to the DDL-created table; override
    /// only when a deployment pipeline manages a differently-named audit table.
    /// </summary>
    public string AuditTableName { get; set; } = "edict_audit_record";

    /// <summary>
    /// Run the embedded Orleans + Edict DDL scripts at silo wiring time. The
    /// scripts are idempotent (Edict tables use <c>CREATE TABLE IF NOT EXISTS</c>;
    /// Orleans tables are skipped if their canonical table already exists), so
    /// the simple path needs no migration tooling. Set to <c>false</c> when a
    /// deployment pipeline already manages the schema.
    /// </summary>
    public bool BootstrapSchema { get; set; } = true;

    /// <summary>
    /// Upper bound on connections held by the shared <c>NpgsqlDataSource</c>
    /// the silo wires at startup. Default <c>200</c> gives a single silo 2×
    /// headroom against the published <c>N = 256</c> closed-loop sweep
    /// point and absorbs the projection/idempotency grain-turn demand that
    /// does not appear in the headline EPS number. Trade-off: each silo's
    /// <c>MaxPoolSize</c> is a multiplier against Postgres
    /// <c>max_connections</c> — a 5-silo fleet at the default demands
    /// <c>1000</c> connections, well above Postgres 16's default
    /// <c>max_connections = 100</c>, so a multi-silo deployment must raise
    /// the Postgres ceiling or lower this knob. Wins over any
    /// <c>Maximum Pool Size</c> keyword in
    /// <see cref="ConnectionString"/> — the options surface is the one
    /// obvious place to tune.
    /// </summary>
    public int MaxPoolSize { get; set; } = 200;

    /// <summary>
    /// Minimum number of connections the shared <c>NpgsqlDataSource</c>
    /// pre-creates at startup. Default <c>10</c> absorbs the slow
    /// <c>create_time</c> tail observed at <c>N = 64</c> (p99 1.31 s per new
    /// pooled connection) so first-burst traffic does
    /// not pay establishment latency. Trade-off: 10 idle TCP sessions per
    /// silo against the Postgres connection budget — cheap on the
    /// pooling axis, negligible on the substrate axis. Wins over any
    /// <c>Minimum Pool Size</c> keyword in <see cref="ConnectionString"/>.
    /// </summary>
    public int MinPoolSize { get; set; } = 10;

    /// <summary>
    /// Total attempts (one try plus retries) the grain-storage seams make against a
    /// transient Npgsql fault before surfacing <c>EdictPostgresStorageException</c>.
    /// Default <c>3</c> (one try, two retries).
    /// <para>
    /// The Azure pairing gets transient-fault resilience for free from the Azure
    /// Storage SDK; Postgres does not, because the provider rolls its own grain
    /// storage (Orleans 10's <c>AdoNetGrainStorage</c> collapses every
    /// <c>Grain&lt;T&gt;</c> sharing a <c>[EdictRouteKey]</c> into one row), so the
    /// retry that the Azure SDK applies silently has to be applied here explicitly.
    /// A transient socket reset that the Azure pairing absorbs would otherwise fail
    /// a Postgres grain activation outright. Retry is gated on
    /// <c>NpgsqlException.IsTransient</c>; a non-transient fault surfaces on the
    /// first attempt with no delay.
    /// </para>
    /// </summary>
    public int StorageRetryCount { get; set; } = 3;

    /// <summary>
    /// Base delay for the grain-storage transient-retry backoff. Default <c>50 ms</c>,
    /// applied exponentially with no jitter (50 ms, then 100 ms).
    /// <para>
    /// Retrying a write is safe even when the first attempt may already have applied:
    /// the version-checked <c>INSERT ... ON CONFLICT DO NOTHING RETURNING</c> /
    /// <c>UPDATE ... WHERE version = @expected RETURNING</c> statements return no row
    /// on a no-op, so a double-apply degrades to an <c>InconsistentStateException</c>,
    /// which Orleans handles by deactivating and re-reading rather than corrupting state.
    /// </para>
    /// </summary>
    public TimeSpan StorageRetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(50);
}
