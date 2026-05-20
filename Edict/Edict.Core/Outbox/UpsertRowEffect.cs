namespace Edict.Core.Outbox;

/// <summary>
/// The <see cref="OutboxEffectKind.UpsertRow"/> payload (ADR 0018): a Table
/// Projection Builder's computed row, captured as a durable pending effect in
/// the same one grain-state write as the dedup-ring commit, then drained
/// at-least-once. Because the whole row travels (not a delta) and the drain is
/// a pk/rk full-row replace, redelivery of the effect is idempotent — this is
/// how ADR 0012's double-apply gap is <b>closed</b>, not merely accepted.
/// <para>
/// The row is a consumer POCO with no Orleans codec on the wire, so it travels
/// as JSON plus its frozen <c>[Alias]</c> literal (ADR 0027): the alias is what
/// the publisher captures via <c>Orleans.Serialization.TypeConverter.Format</c>
/// and the drain resolves via <c>TypeConverter.Parse</c>, so a consumer who
/// renames the row POCO class — but preserves its <c>[Alias]</c> — has no
/// in-flight entries dead-lettered. Closes the AQTN hole called out in
/// <c>resiliency-analysis.md</c> §3.2.3. Persisted state, so a frozen
/// string-literal <c>[Alias]</c> survives a class rename (ADR 0017);
/// <c>ORLEANS0010</c> is never suppressed. Bare-named — no consumer types it.
/// </para>
/// </summary>
[GenerateSerializer]
[Alias("UpsertRowEffect")]
public sealed record UpsertRowEffect
{
    [Id(0)]
    public string TableName { get; init; } = "";

    [Id(1)]
    public string PartitionKey { get; init; } = "";

    [Id(2)]
    public string RowKey { get; init; } = "";

    /// <summary>
    /// Frozen <c>[Alias]</c> literal of the consumer row POCO, captured via
    /// <c>Orleans.Serialization.TypeConverter.Format(typeof(T))</c>. The drain
    /// resolves it back to the concrete <see cref="System.Type"/> via
    /// <c>TypeConverter.Parse</c> against the Orleans manifest, so a class
    /// rename that preserves the alias does not dead-letter (ADR 0027).
    /// </summary>
    [Id(3)]
    public string RowAlias { get; init; } = "";

    /// <summary>The row serialized as UTF-8 JSON (the POCO carries no Orleans codec).</summary>
    [Id(4)]
    public byte[] RowJson { get; init; } = [];
}
