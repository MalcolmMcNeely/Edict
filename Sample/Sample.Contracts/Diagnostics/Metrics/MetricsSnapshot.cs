namespace Sample.Contracts.Diagnostics.Metrics;

/// <summary>
/// A point-in-time view of the four headline operator metrics, returned by
/// <see cref="IEdictMetricsProbeGrain.GetSnapshotAsync"/>. The Live Metrics
/// page polls the probe grain at 1Hz and renders one tile per field.
/// </summary>
[GenerateSerializer]
[Alias("Sample.Contracts.Diagnostics.Metrics.MetricsSnapshot")]
public sealed record MetricsSnapshot(
    [property: Id(0)] int OutboxPendingSum,
    [property: Id(1)] double OutboxOldestAgeSeconds,
    [property: Id(2)] double DeadLetterPromotionsPerSecond,
    [property: Id(3)] double EventHandleDurationP99Seconds,
    [property: Id(4)] double EventHandleLagP99Seconds);
