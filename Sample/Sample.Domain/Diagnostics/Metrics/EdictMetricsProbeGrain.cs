using Edict.Core.Metrics;

using Orleans;

using Sample.Contracts.Diagnostics.Metrics;

namespace Sample.Domain.Diagnostics.Metrics;

/// <summary>
/// Singleton grain backing the Live Metrics spoke. Reads the gauge-shaped
/// metrics from the silo-local <see cref="IEdictMetricsCache"/> (ADR-0040) and
/// the histogram / counter aggregates from
/// <see cref="EdictMetricsAggregator"/>. The grain itself is stateless — its
/// activation is purely a routing hop into the silo process so the Web
/// frontend can read silo-side telemetry without scraping <c>/metrics</c>.
/// </summary>
public sealed class EdictMetricsProbeGrain : Grain, IEdictMetricsProbeGrain
{
    readonly IEdictMetricsCache _cache;
    readonly EdictMetricsAggregator _aggregator;
    readonly TimeProvider _timeProvider;

    public EdictMetricsProbeGrain(
        IEdictMetricsCache cache,
        EdictMetricsAggregator aggregator,
        TimeProvider timeProvider)
    {
        _cache = cache;
        _aggregator = aggregator;
        _timeProvider = timeProvider;
    }

    public Task<MetricsSnapshot> GetSnapshotAsync()
    {
        var (pendingSum, oldest) = _cache.GetOutboxStateAggregate();
        var oldestAgeSeconds = oldest is null
            ? 0
            : Math.Max(0, (_timeProvider.GetUtcNow() - oldest.Value).TotalSeconds);

        var snapshot = new MetricsSnapshot(
            OutboxPendingSum: pendingSum,
            OutboxOldestAgeSeconds: oldestAgeSeconds,
            DeadLetterPromotionsPerSecond: _aggregator.DeadLetterPromotionsPerSecond(),
            EventHandleDurationP99Seconds: _aggregator.EventHandleDurationP99Seconds(),
            EventHandleLagP99Seconds: _aggregator.EventHandleLagP99Seconds());

        return Task.FromResult(snapshot);
    }
}
