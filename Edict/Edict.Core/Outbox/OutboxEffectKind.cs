namespace Edict.Core.Outbox;

/// <summary>
/// The three outbound side-effects the single Outbox engine drains (ADR 0018).
/// Persisted inside <see cref="OutboxEntry"/>; Orleans serializes enums natively.
/// </summary>
public enum OutboxEffectKind
{
    PublishEvent,
    SendCommand,
    UpsertRow,
}
