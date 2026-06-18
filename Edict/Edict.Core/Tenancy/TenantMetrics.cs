using System.Diagnostics.Metrics;

using Edict.Telemetry;

namespace Edict.Core.Tenancy;

// Tenant-isolation instruments on the "Edict" meter. Held in a non-generic static so the
// initializer runs once per process, not once per closed generic of the host. The only
// tag is a closed outcome allowlist — never the tenant value, which is unbounded and
// lives on the span event.
static class TenantMetrics
{
    public static readonly Counter<long> CrossingCount = EdictDiagnostics.Meter.CreateCounter<long>(
        SemanticConventions.Tenant.Meters.CrossingCount);
}
