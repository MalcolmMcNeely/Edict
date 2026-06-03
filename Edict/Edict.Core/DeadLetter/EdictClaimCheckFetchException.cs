namespace Edict.Core.DeadLetter;

/// <summary>
/// Runtime fault raised when a receiver-side claim-check lookup cannot produce
/// a payload for an event's <see cref="EventId"/>. The type's existence means
/// "claim-check payload missing" — a body that was never written or was
/// lifecycle-reaped — and the dead-letter classifier maps the whole type to
/// the <c>Substrate</c> bucket. There is no "malformed key" mode: the key is
/// the event's <see cref="Guid"/> id, which is always well-formed.
/// </summary>
public sealed class EdictClaimCheckFetchException : Exception
{
    public EdictClaimCheckFetchException(Guid eventId, string message)
        : base(message)
    {
        EventId = eventId;
    }

    public Guid EventId { get; }
}
