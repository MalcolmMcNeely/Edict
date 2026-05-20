namespace Edict.Core.Outbox;

/// <summary>
/// The outbound side-effects the single Outbox engine drains (ADR 0018, ADR 0023).
/// Persisted inside <see cref="OutboxEntry"/>; Orleans serializes enums natively
/// by ordinal, so values must be appended — never reordered or removed (ADR 0010).
/// </summary>
public enum OutboxEffectKind
{
    PublishEvent,
    SendCommand,
    UpsertRow,
    InvokeHandler,
}
