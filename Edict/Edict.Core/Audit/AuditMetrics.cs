using System.Diagnostics.Metrics;

using Edict.Telemetry;

namespace Edict.Core.Audit;

// Audit instruments on the "Edict" meter. Held in a non-generic static so the
// initializer runs once per process, not once per closed generic of the host.
// Tags are closed allowlists (kind, outcome, grain type) only — never the
// principal, correlation id, or grain key, which are unbounded and live on spans.
static class AuditMetrics
{
    public static readonly Counter<long> RecordsCaptured = EdictDiagnostics.Meter.CreateCounter<long>(
        SemanticConventions.Audit.Meters.RecordsCaptured);

    public static readonly Counter<long> DrainFailure = EdictDiagnostics.Meter.CreateCounter<long>(
        SemanticConventions.Audit.Meters.DrainFailure);
}
