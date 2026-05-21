using System.Diagnostics;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Events;
using Edict.Core.Outbox;
using Edict.Telemetry;

using Orleans.Serialization;

namespace Edict.Core.ClaimCheck;

/// <summary>
/// Publisher-side decision point at the Outbox commit boundary.
/// Serialises the buffered event, measures the byte length, and
/// either rides it on the wire as itself (under threshold) or uploads the
/// body to <see cref="IEdictClaimCheckStore"/> and substitutes a pointer
/// <see cref="EdictEventEnvelope"/> (over threshold). Conditional-wrap shape:
/// small events stay raw so the existing PublishEventExecutor /
/// stream-observer surface is unchanged until slice 3 wires receiver-side
/// unwrap.
/// <para>
/// Deep module: a single <see cref="ApplyAsync"/> entry point folds
/// serialisation, threshold gating, the optional blob put, envelope wrapping
/// via <see cref="EnvelopeCodec"/>, and the post-wrap framing assertion
/// behind one async call. Pure-ish — no Orleans runtime, no DI beyond its
/// own constructor — so it is callable from xUnit with a fake store.
/// </para>
/// </summary>
public sealed class ClaimCheckPolicy
{
    /// <summary>Azure Table per-property cap; the post-wrap envelope must fit.</summary>
    internal const int MaxEnvelopeBytes = 32_768;

    readonly Serializer _serializer;
    readonly int _thresholdBytes;
    readonly IEdictClaimCheckStore? _store;

    public ClaimCheckPolicy(Serializer serializer, int thresholdBytes, IEdictClaimCheckStore? store)
    {
        _serializer = serializer;
        _thresholdBytes = thresholdBytes;
        _store = store;
    }

    /// <summary>
    /// Returns the bytes to persist as the <see cref="OutboxEntry.Payload"/>
    /// for the supplied event. Under threshold returns the serialised inner
    /// event verbatim; over threshold uploads the body and returns the
    /// serialised pointer envelope. Throws <see cref="EdictEnvelopeOverflowException"/>
    /// when the wrapped envelope still exceeds <see cref="MaxEnvelopeBytes"/>.
    /// </summary>
    public async Task<byte[]> ApplyAsync(EdictEvent evt, CancellationToken ct)
    {
        var innerBytes = _serializer.SerializeToArray<EdictEvent>(evt);
        if (innerBytes.Length <= _thresholdBytes)
        {
            return innerBytes;
        }

        if (_store is null)
        {
            throw new InvalidOperationException(
                $"Event '{evt.GetType().FullName}' serialised to {innerBytes.Length} bytes, exceeding the claim-check threshold "
                + $"of {_thresholdBytes}, but no IEdictClaimCheckStore is registered. "
                + "Register the Azure provider's AzureBlobClaimCheckStore (or the in-memory store in Edict.Testing) "
                + "via DI so oversized events can be uploaded.");
        }

        var key = await PutAsync(evt, innerBytes, ct);

        var envelope = EnvelopeCodec.WrapPointer(key) with
        {
            InnerEventStreamName = EventStreamAddress.Resolve(evt).StreamName,
            InnerEventRouteKey = EventStreamAddress.Resolve(evt).RouteKey,
        };
        var envelopeBytes = _serializer.SerializeToArray<EdictEvent>(envelope);
        if (envelopeBytes.Length > MaxEnvelopeBytes)
        {
            var (_, routeKey) = EventStreamAddress.Resolve(evt);
            throw new EdictEnvelopeOverflowException(
                routeKey, evt.GetType().FullName!, envelopeBytes.Length);
        }

        Activity.Current?.SetTag("edict.event.claimChecked", true);

        return envelopeBytes;
    }

    async Task<string> PutAsync(EdictEvent evt, byte[] innerBytes, CancellationToken ct)
    {
        using var span = EdictDiagnostics.ActivitySource.StartActivity(
            "edict.event.claim_check.put", ActivityKind.Client);
        span?.SetTag("edict.event.type", evt.GetType().Name);
        span?.SetTag("edict.event.size_bytes", innerBytes.Length);

        var key = await _store!.PutAsync(innerBytes, ct);
        span?.SetTag("edict.claim_check.key", key);
        return key;
    }
}
