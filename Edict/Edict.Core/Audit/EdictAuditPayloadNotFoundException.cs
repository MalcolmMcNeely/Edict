namespace Edict.Core.Audit;

/// <summary>
/// Raised when <see cref="Contracts.Audit.IEdictAuditRepository.GetPayloadAsync"/>
/// finds no captured body for a record id. The type's existence means "audit
/// payload missing" — a record id that never had a body, or one outside the
/// chain. The body store is append-only and infinite-retention, so for a record
/// that exists this should never happen; it is the honest signal when a caller
/// asks for an id the store does not hold.
/// </summary>
public sealed class EdictAuditPayloadNotFoundException : Exception
{
    public EdictAuditPayloadNotFoundException(Guid recordId, string message)
        : base(message)
    {
        RecordId = recordId;
    }

    public Guid RecordId { get; }
}
