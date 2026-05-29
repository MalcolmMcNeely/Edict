using Orleans;

namespace Sample.Contracts.Diagnostics.Metrics;

/// <summary>
/// Singleton silo-side probe that exposes the four headline operator metrics
/// for the Sample.Web Live Metrics spoke. The grain reads from
/// <c>IEdictMetricsCache</c> (silo-local cache, ADR-0040) and a
/// silo-process MeterListener that aggregates histogram + counter samples,
/// returning the result as a single <see cref="MetricsSnapshot"/> per call.
/// One grain call per Web-page tick keeps the Hub-and-spoke IA's
/// "no Prometheus container" promise without paying the per-grain scrape
/// fan-out ADR-0040 warned against.
/// </summary>
public interface IEdictMetricsProbeGrain : IGrainWithGuidKey
{
    Task<MetricsSnapshot> GetSnapshotAsync();
}
