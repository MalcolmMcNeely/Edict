using System.Diagnostics;
using System.Diagnostics.Metrics;

using Edict.Contracts.Events;
using Edict.Core.ClaimCheck;
using Edict.Core.Outbox;
using Edict.Telemetry;

using Orleans.Serialization;
using Orleans.Streams;

namespace Edict.Core.EventHandler;

sealed class InvokeHandlerExecutor(
    Serializer serializer,
    ClaimCheckUnwrap unwrap,
    IEventTagWriters tagWriters,
    TimeProvider timeProvider) : IOutboxEffectExecutor
{
    static readonly Histogram<double> HandleDuration = EdictDiagnostics.Meter.CreateHistogram<double>(
        SemanticConventions.Events.Meters.HandleDuration);

    static readonly Histogram<double> HandleLag = EdictDiagnostics.Meter.CreateHistogram<double>(
        SemanticConventions.Events.Meters.HandleLag);

    public OutboxEffectKind Kind => OutboxEffectKind.InvokeHandler;

    public async Task<OutboxEntry?> ExecuteAsync(
        OutboxEntry entry,
        IStreamProvider streamProvider,
        Func<EdictEvent, Task<OutboxEntry?>>? deferredDispatch,
        Type? consumerType,
        EdictEvent? liveWireEvent)
    {
        var staged = serializer.Deserialize<EdictEvent>(entry.Payload);

        // Open the consumer turn first so the claim-check fetch nests under it; a
        // pointer envelope's inner type is unknown until the fetch returns, so the
        // span starts named for the staged frame and its DisplayName is corrected
        // once unwrapped. The common raw-event path stages the event itself, so the
        // name is already right and the rename is a no-op; only an oversized
        // (pointer-envelope) event leaves OperationName on the wrapper while the
        // exported DisplayName carries the inner type.
        var link = ActivityExtensions.BuildLink(entry.TraceParent, entry.TraceState);
        using var span = EdictDiagnostics.ActivitySource.StartEdictEventHandle(
            staged.GetType().Name, link);

        var materialised = await unwrap.ApplyAsync(
            staged, consumerType ?? typeof(object), span?.Context ?? default, CancellationToken.None);

        if (span is not null)
        {
            span.DisplayName = $"{SemanticConventions.Events.Spans.Handle} {materialised.GetType().Name}";
            if (tagWriters.TryGet(materialised.GetType(), out var write))
            {
                write(materialised, span);
            }
        }

        var eventTypeTag = new KeyValuePair<string, object?>(
            SemanticConventions.Events.Tags.Type, materialised.GetType().Name);
        var grainTypeTag = new KeyValuePair<string, object?>(
            SemanticConventions.Common.Tags.GrainType, (consumerType ?? typeof(object)).FullName);

        HandleLag.Record(
            Math.Max(0, (timeProvider.GetUtcNow() - materialised.OccurredAt).TotalSeconds),
            eventTypeTag,
            grainTypeTag);

        var startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            // A deferred saga / table-projection dispatch stages its downstream
            // effect (SendCommand / UpsertRow) and returns it here; the host
            // enqueues it into the same write that acks this InvokeHandler
            // entry. Event handlers and the in-memory projection builder return
            // null.
            return await deferredDispatch!(materialised);
        }
        finally
        {
            HandleDuration.Record(
                Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds,
                eventTypeTag,
                grainTypeTag);
        }
    }
}
