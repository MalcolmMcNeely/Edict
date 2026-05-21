namespace Edict.Core.Outbox;

/// <summary>
/// The outbound side-effects the single Outbox engine drains.
/// Persisted inside <see cref="OutboxEntry"/>; Orleans serializes enums natively
/// by ordinal, so values must be appended — never reordered or removed.
/// </summary>
public enum OutboxEffectKind
{
    PublishEvent,
    SendCommand,
    UpsertRow,
    InvokeHandler,
}
