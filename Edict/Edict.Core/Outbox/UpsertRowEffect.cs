using System.ComponentModel;

namespace Edict.Core.Outbox;

/// <summary>
/// The <see cref="OutboxEffectKind.UpsertRow"/> payload: a Table Projection
/// Builder's computed row, captured as a durable pending effect in
/// the same one grain-state write as the dedup-ring commit, then drained
/// at-least-once. Because the whole row travels (not a delta) and the drain is
/// a pk/rk full-row replace, redelivery of the effect is idempotent — this is
/// how the table-projection double-apply gap is <b>closed</b>, not merely accepted.
/// <para>
/// The row is encoded with the Orleans <c>Serializer</c> end-to-end — the row
/// POCO carries <c>[GenerateSerializer]</c> via <c>IEdictPersistedState</c>.
/// Its type identity travels as its frozen <c>[Alias]</c> literal,
/// captured by the publisher via <c>Orleans.Serialization.TypeConverter.Format</c>
/// and resolved by the drain via <c>TypeConverter.Parse</c>, so a consumer
/// who renames the row POCO class — but preserves its <c>[Alias]</c> — has no
/// in-flight entries dead-lettered. <c>ORLEANS0010</c> is never suppressed.
/// Bare-named — no consumer types it.
/// </para>
/// <para>
/// Public because the framework deserializes it from
/// <see cref="OutboxEntry.Payload"/> bytes on drain; hidden from consumer
/// IntelliSense because the consumer never types this — the projection-builder
/// base stages the effect on the consumer's behalf.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
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
    /// rename that preserves the alias does not dead-letter.
    /// </summary>
    [Id(3)]
    public string RowAlias { get; init; } = "";

    /// <summary>The row serialized with the Orleans <c>Serializer</c>; decoded at drain via <c>Serializer.Deserialize(rowType, bytes)</c>.</summary>
    [Id(4)]
    public byte[] RowBytes { get; init; } = [];
}
