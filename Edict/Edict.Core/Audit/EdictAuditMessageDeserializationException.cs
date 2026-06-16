namespace Edict.Core.Audit;

/// <summary>
/// Raised when <see cref="Contracts.Audit.IEdictAuditRepository.GetMessageAsync"/>
/// cannot turn a captured body back into its concrete message in the current
/// process: the type was renamed, removed, or breaking-changed, its assembly is
/// not loaded, or the bytes no longer deserialize. Carries the
/// <see cref="RecordId"/> and the captured <see cref="MessageType"/> so a caller
/// knows which record and which type failed. The body and its hash stay
/// retrievable through <see cref="Contracts.Audit.IEdictAuditRepository.GetPayloadAsync"/>,
/// so the typed read failing never costs the integrity anchor.
/// </summary>
public sealed class EdictAuditMessageDeserializationException : Exception
{
    public EdictAuditMessageDeserializationException(Guid recordId, string messageType, Exception innerException)
        : base($"Could not deserialize the audit body for record '{recordId:N}' captured as '{messageType}'.", innerException)
    {
        RecordId = recordId;
        MessageType = messageType;
    }

    public Guid RecordId { get; }

    public string MessageType { get; }
}
