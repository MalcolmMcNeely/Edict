using Edict.Contracts.Events;

namespace Edict.Core.ClaimCheck;

/// <summary>
/// Pure, no-I/O helpers for constructing and discriminating
/// <see cref="EdictEventEnvelope"/> instances (ADR 0024). Single source of
/// truth for the exactly-one-branch invariant — both <c>Wrap*</c> entry
/// points delegate to the envelope's validating constructor, so neither
/// the publisher's commit pipeline nor the receiver's unwrap step needs
/// to know the invariant exists. Internal to the runtime; consumer code
/// never references the envelope or this codec.
/// </summary>
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

    public static bool IsEnvelope(EdictEvent evt) => evt is EdictEventEnvelope;

    public static byte[]? TryGetInline(EdictEventEnvelope envelope) => envelope.InlinePayload;

    public static string? TryGetPointer(EdictEventEnvelope envelope) => envelope.ClaimCheckKey;
}
