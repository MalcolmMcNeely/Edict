using Edict.Contracts.Events;

namespace Edict.Core.ClaimCheck;

static class EnvelopeCodec
{
    public static EdictEventEnvelope WrapInline(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return new EdictEventEnvelope(bytes, Guid.Empty);
    }

    public static EdictEventEnvelope WrapPointer(Guid eventId)
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Claim-check pointer envelope requires a non-empty EventId.", nameof(eventId));
        }

        return new EdictEventEnvelope(null, eventId);
    }

    public static bool IsEnvelope(EdictEvent edictEvent) => edictEvent is EdictEventEnvelope;

    public static byte[]? TryGetInline(EdictEventEnvelope envelope) => envelope.InlinePayload;
}
