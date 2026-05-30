using System.ComponentModel;

namespace Edict.Core.Outbox;

/// <summary>
/// The outbound side-effects the single Outbox engine drains.
/// Persisted inside <see cref="OutboxEntry"/>; Orleans serializes enums natively
/// by ordinal, so values must be appended — never reordered or removed.
/// <para>
/// Public because it rides as the <see cref="OutboxEntry.Kind"/> property on
/// the public <see cref="OutboxEntry"/>. Hidden from consumer IntelliSense
/// because the consumer never names this — the engine owns the effect taxonomy.
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public enum OutboxEffectKind
{
    PublishEvent,
    SendCommand,
    UpsertRow,
    InvokeHandler,
}
