using System.Diagnostics;
using System.Diagnostics.Metrics;

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

    static readonly Histogram<long> PayloadSize = EdictDiagnostics.Meter.CreateHistogram<long>(
        SemanticConventions.ClaimCheck.Meters.PayloadSize);

    readonly Serializer _serializer;
    readonly int _thresholdBytes;
    readonly IEdictClaimCheckStore? _store;
    readonly IEventStreamAccessors _accessors;

    public ClaimCheckPolicy(Serializer serializer, int thresholdBytes, IEdictClaimCheckStore? store, IEventStreamAccessors accessors)
    {
        _serializer = serializer;
        _thresholdBytes = thresholdBytes;
        _store = store;
        _accessors = accessors;
    }

    /// <summary>
    /// Returns the bytes to persist as the <see cref="OutboxEntry.Payload"/>
    /// for the supplied event, paired with the live <see cref="EdictEvent"/>
    /// those bytes round-trip to (the original event under threshold, or the
    /// constructed pointer envelope over threshold). The inline-drain path uses
    /// <see cref="ClaimCheckApplyResult.WireEvent"/> to skip a redundant
    /// deserialise; crash-recovery drains have no live ref and deserialise the
    /// stored payload as before. Throws <see cref="EdictEnvelopeOverflowException"/>
    /// when the wrapped envelope still exceeds <see cref="MaxEnvelopeBytes"/>.
    /// </summary>
    public async Task<ClaimCheckApplyResult> ApplyAsync(EdictEvent evt, CancellationToken ct)
    {
        var innerBytes = _serializer.SerializeToArray<EdictEvent>(evt);
        if (innerBytes.Length <= _thresholdBytes)
        {
            PayloadSize.Record(innerBytes.Length,
                new KeyValuePair<string, object?>(SemanticConventions.Events.Tags.Type, evt.GetType().Name),
                new KeyValuePair<string, object?>(SemanticConventions.Events.Tags.ClaimChecked, false));
            return new ClaimCheckApplyResult(innerBytes, evt);
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

        var (innerStreamName, innerRouteKey) = _accessors.Resolve(evt);
        var envelope = EnvelopeCodec.WrapPointer(key) with
        {
            InnerEventStreamName = innerStreamName,
            InnerEventRouteKey = innerRouteKey,
        };
        var envelopeBytes = _serializer.SerializeToArray<EdictEvent>(envelope);
        if (envelopeBytes.Length > MaxEnvelopeBytes)
        {
            throw new EdictEnvelopeOverflowException(
                innerRouteKey, evt.GetType().FullName!, envelopeBytes.Length);
        }

        Activity.Current?.SetTag(SemanticConventions.Events.Tags.ClaimChecked, true);

        PayloadSize.Record(innerBytes.Length,
            new KeyValuePair<string, object?>(SemanticConventions.Events.Tags.Type, evt.GetType().Name),
            new KeyValuePair<string, object?>(SemanticConventions.Events.Tags.ClaimChecked, true));

        return new ClaimCheckApplyResult(envelopeBytes, envelope);
    }

    /// <summary>
    /// Outcome of <see cref="ApplyAsync"/>: the bytes to persist on the outbox
    /// entry plus the live <see cref="EdictEvent"/> the bytes deserialise to.
    /// The inline drain consumes <see cref="WireEvent"/> directly so the
    /// happy path avoids a serialise→deserialise round trip per raise.
    /// </summary>
    public readonly record struct ClaimCheckApplyResult(byte[] Payload, EdictEvent WireEvent);

    async Task<string> PutAsync(EdictEvent evt, byte[] innerBytes, CancellationToken ct)
    {
        using var span = EdictDiagnostics.ActivitySource.StartActivity(
            SemanticConventions.ClaimCheck.Spans.Put, ActivityKind.Client);
        span?.SetTag(SemanticConventions.Events.Tags.Type, evt.GetType().Name);
        span?.SetTag(SemanticConventions.Events.Tags.SizeBytes, innerBytes.Length);

        var key = await _store!.PutAsync(innerBytes, ct);
        span?.SetTag(SemanticConventions.ClaimCheck.Tags.Key, key);
        return key;
    }
}
