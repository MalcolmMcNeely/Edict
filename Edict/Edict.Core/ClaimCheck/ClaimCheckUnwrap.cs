using System.Diagnostics;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Events;
using Edict.Telemetry;

using Orleans.Serialization;

namespace Edict.Core.ClaimCheck;

internal sealed class ClaimCheckUnwrap
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

    public async Task<EdictEvent> ApplyAsync(
        EdictEvent incoming, Type consumerType, ActivityContext parentContext, CancellationToken cancellationToken)
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

        var eventId = envelope.EventId;
        // Nest the blob fetch under the consumer turn from the explicit context:
        // at handle entry Activity.Current is null, so a bare StartActivity would
        // spawn a detached root for every claim-checked event.
        using var span = EdictDiagnostics.ActivitySource.StartActivity(
            SemanticConventions.ClaimCheck.Spans.Get, ActivityKind.Client, parentContext);
        span?.SetTag(SemanticConventions.ClaimCheck.Tags.Key, eventId.ToString());

        var bytes = await _store!.GetAsync(eventId, cancellationToken);
        span?.SetTag(SemanticConventions.Events.Tags.SizeBytes, bytes.Length);

        var inner = _serializer.Deserialize<EdictEvent>(bytes.ToArray());
        span?.SetTag(SemanticConventions.Events.Tags.Type, inner.GetType().Name);
        return inner;
    }
}
