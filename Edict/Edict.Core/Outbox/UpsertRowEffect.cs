namespace Edict.Core.Outbox;

/// <summary>
/// The <see cref="OutboxEffectKind.UpsertRow"/> payload (ADR 0018): a Table
/// Projection Builder's computed row, captured as a durable pending effect in
/// the same one grain-state write as the dedup-ring commit, then drained
/// at-least-once. Because the whole row travels (not a delta) and the drain is
/// a pk/rk full-row replace, redelivery of the effect is idempotent — this is
/// how ADR 0012's double-apply gap is <b>closed</b>, not merely accepted.
/// <para>
/// The row is a consumer POCO (<c>T : class, new()</c>) with no Orleans codec,
/// so it travels as JSON plus its assembly-qualified type name; the envelope
/// itself is Orleans-serialized into <see cref="OutboxEntry.Payload"/> like the
/// <see cref="OutboxEffectKind.PublishEvent"/> case. Persisted state, so a
/// frozen string-literal <c>[Alias]</c> survives a class rename (ADR 0017);
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

    /// <summary>Assembly-qualified name of the consumer row POCO, so the drain reconstructs the concrete type.</summary>
    [Id(3)]
    public string RowTypeName { get; init; } = "";

    /// <summary>The row serialized as UTF-8 JSON (the POCO carries no Orleans codec).</summary>
    [Id(4)]
    public byte[] RowJson { get; init; } = [];
}
