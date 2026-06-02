using System.Diagnostics.Metrics;

using Edict.Telemetry;

namespace Edict.Core.Sagas;

// Non-generic counter holder so the static initialiser runs once per process
// (not once per closed EdictSaga<TProgress>) and the architecture-test walk over
// static instrument fields discovers both counters. Mirrors IdempotencyDedupMetrics.
static class SagaLifecycleMetrics
{
    static readonly Counter<long> TimeoutFired = EdictDiagnostics.Meter.CreateCounter<long>(
        SemanticConventions.Sagas.Meters.TimeoutFired);

    static readonly Counter<long> Completed = EdictDiagnostics.Meter.CreateCounter<long>(
        SemanticConventions.Sagas.Meters.Completed);

    public static void EmitTimeoutFired(string sagaType, string outcome)
    {
        TimeoutFired.Add(1,
            new KeyValuePair<string, object?>(SemanticConventions.Common.Tags.GrainType, sagaType),
            new KeyValuePair<string, object?>(SemanticConventions.Sagas.Tags.Outcome, outcome));
    }

    public static void EmitCompleted(string sagaType)
    {
        Completed.Add(1,
            new KeyValuePair<string, object?>(SemanticConventions.Common.Tags.GrainType, sagaType));
    }
}
