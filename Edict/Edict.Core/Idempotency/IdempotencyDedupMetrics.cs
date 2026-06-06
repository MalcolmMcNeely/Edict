using System.Diagnostics;
using System.Diagnostics.Metrics;

using Edict.Contracts.Events;
using Edict.Telemetry;

namespace Edict.Core.Idempotency;

static class IdempotencyDedupMetrics
{
    static readonly Counter<long> DuplicateCount = EdictDiagnostics.Meter.CreateCounter<long>(
        SemanticConventions.Idempotency.Meters.DuplicateCount);

    // The duplicate counter is recorded while the dedup span is current so the
    // TraceBased exemplar filter attaches the suppressed-redelivery trace; a
    // measurement taken after the span disposed would carry no exemplar.
    public static void EmitDedupSpanAndHit(EdictEvent edictEvent, string grainTypeName)
    {
        using var span = StartDedupSpan(edictEvent);
        EmitDedupHit(edictEvent, grainTypeName);
    }

    // EdictEventHandler suppresses a redelivery with the dedup span alone, without
    // the duplicate counter the idempotency base and saga paths emit.
    public static void EmitDedupSpan(EdictEvent edictEvent)
    {
        using var span = StartDedupSpan(edictEvent);
    }

    public static void EmitDedupHit(EdictEvent edictEvent, string grainTypeName)
    {
        DuplicateCount.Add(1,
            new KeyValuePair<string, object?>(SemanticConventions.Events.Tags.Type, edictEvent.GetType().Name),
            new KeyValuePair<string, object?>(SemanticConventions.Common.Tags.GrainType, grainTypeName));
    }

    static Activity? StartDedupSpan(EdictEvent edictEvent)
    {
        var link = ActivityExtensions.BuildLink(edictEvent.TraceId, edictEvent.SpanId, edictEvent.TraceState);
        var span = EdictDiagnostics.ActivitySource.StartEdictEventDeduplicated(edictEvent.GetType().Name, link);
        span?.SetTag(SemanticConventions.Events.Tags.Deduplicated, true);
        return span;
    }
}
