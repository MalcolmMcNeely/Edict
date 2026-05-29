using System.Diagnostics;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Events;
using Edict.Telemetry;

using Orleans.Serialization;

namespace Edict.Core.ClaimCheck;

/// <summary>
/// Receiver-side decision point at the stream-observer hop.
/// Given an incoming <see cref="EdictEvent"/> and the consumer's CLR type,
/// returns the concrete event to dispatch to <c>Handle</c>: passthrough for
/// non-envelopes, deserialise from inline bytes for inline-branch envelopes,
/// or fetch via <see cref="IEdictClaimCheckStore"/> for pointer-branch
/// envelopes. The store fetch is gated by a per-consumer-type predicate (the
/// dead-letter projection's pointer-preserving special case lands on this
/// seam). Missing-blob surfaces from the store unchanged — the
/// caller (the stream-observer in <c>EdictIdempotencyBase</c>) treats it as a
/// transient delivery failure and runs the receiver-side dead-letter promotion.
/// </summary>
public sealed class ClaimCheckUnwrap
{
    readonly Serializer _serializer;
    readonly IEdictClaimCheckStore? _store;
    readonly Func<Type, bool> _shouldFetchForConsumer;

    public ClaimCheckUnwrap(
        Serializer serializer,
        IEdictClaimCheckStore? store,
        Func<Type, bool>? shouldFetchForConsumer = null)
    {
        _serializer = serializer;
        _store = store;
        _shouldFetchForConsumer = shouldFetchForConsumer ?? (_ => true);
    }

    public async Task<EdictEvent> ApplyAsync(EdictEvent incoming, Type consumerType, CancellationToken cancellationToken)
    {
        if (incoming is not EdictEventEnvelope envelope)
        {
            return incoming;
        }

        if (envelope.InlinePayload is { } inline)
        {
            return _serializer.Deserialize<EdictEvent>(inline);
        }

        if (!_shouldFetchForConsumer(consumerType))
        {
            return envelope;
        }

        if (_store is null)
        {
            throw new InvalidOperationException(
                "ClaimCheckUnwrap received a pointer-bearing EdictEventEnvelope but no IEdictClaimCheckStore is registered.");
        }

        var key = envelope.ClaimCheckKey!;
        using var span = EdictDiagnostics.ActivitySource.StartActivity(
            SemanticConventions.ClaimCheck.Spans.Get, ActivityKind.Client);
        span?.SetTag(SemanticConventions.ClaimCheck.Tags.Key, key);

        var bytes = await _store.GetAsync(key, cancellationToken);
        span?.SetTag(SemanticConventions.Events.Tags.SizeBytes, bytes.Length);

        var inner = _serializer.Deserialize<EdictEvent>(bytes.ToArray());
        span?.SetTag(SemanticConventions.Events.Tags.Type, inner.GetType().Name);
        return inner;
    }
}
