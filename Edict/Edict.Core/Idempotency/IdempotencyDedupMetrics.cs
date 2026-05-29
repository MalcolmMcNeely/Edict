using System.Diagnostics.Metrics;

using Edict.Contracts.Events;
using Edict.Telemetry;

namespace Edict.Core.Idempotency;

static class IdempotencyDedupMetrics
{
    static readonly Counter<long> DuplicateCount = EdictDiagnostics.Meter.CreateCounter<long>(
        SemanticConventions.Idempotency.Meters.DuplicateCount);

    public static void EmitDedupHit(EdictEvent evt, string grainTypeName)
    {
        DuplicateCount.Add(1,
            new KeyValuePair<string, object?>(SemanticConventions.Events.Tags.Type, evt.GetType().Name),
            new KeyValuePair<string, object?>(SemanticConventions.Common.Tags.GrainType, grainTypeName));
    }
}
