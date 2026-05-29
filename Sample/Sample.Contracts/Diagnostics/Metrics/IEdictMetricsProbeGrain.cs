using Orleans;

namespace Sample.Contracts.Diagnostics.Metrics;

/// <summary>
/// Singleton silo-side probe that exposes the four headline operator metrics
/// for the Sample.Web Live Metrics spoke. The grain reads from the silo-local
/// <c>IEdictMetricsCache</c> and a silo-process MeterListener that aggregates
/// histogram + counter samples, returning the result as a single
/// <see cref="MetricsSnapshot"/> per call. One grain call per Web-page tick
/// keeps the Hub-and-spoke IA's "no Prometheus container" promise without
/// paying a per-grain scrape fan-out.
/// </summary>
public interface IEdictMetricsProbeGrain : IGrainWithGuidKey
{
    Task<MetricsSnapshot> GetSnapshotAsync();
}
