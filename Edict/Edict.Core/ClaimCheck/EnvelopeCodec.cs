using Edict.Contracts.Events;

namespace Edict.Core.ClaimCheck;

static class EnvelopeCodec
{
    public static EdictEventEnvelope WrapInline(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return new EdictEventEnvelope(bytes, null);
    }

    public static EdictEventEnvelope WrapPointer(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        return new EdictEventEnvelope(null, key);
    }

    public static bool IsEnvelope(EdictEvent edictEvent) => edictEvent is EdictEventEnvelope;

    public static byte[]? TryGetInline(EdictEventEnvelope envelope) => envelope.InlinePayload;

    public static string? TryGetPointer(EdictEventEnvelope envelope) => envelope.ClaimCheckKey;
}
