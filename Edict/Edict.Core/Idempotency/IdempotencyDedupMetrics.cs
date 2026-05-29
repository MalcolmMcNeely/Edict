using System.Diagnostics.Metrics;

using Edict.Contracts.Events;
using Edict.Telemetry;

namespace Edict.Core.Idempotency;

static class IdempotencyDedupMetrics
{
    static readonly Counter<long> DuplicateCount = EdictDiagnostics.Meter.CreateCounter<long>(
        SemanticConventions.Idempotency.Meters.DuplicateCount);

    public static void EmitDedupHit(EdictEvent edictEvent, string grainTypeName)
    {
        DuplicateCount.Add(1,
            new KeyValuePair<string, object?>(SemanticConventions.Events.Tags.Type, edictEvent.GetType().Name),
            new KeyValuePair<string, object?>(SemanticConventions.Common.Tags.GrainType, grainTypeName));
    }
}
