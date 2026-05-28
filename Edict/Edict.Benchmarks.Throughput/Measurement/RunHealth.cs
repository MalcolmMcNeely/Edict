using System.Collections.Immutable;
using System.Globalization;

namespace Edict.Benchmarks.Throughput.Measurement;

/// <summary>
/// Outcome counts for a single sweep point's issuer fan-out. Carries enough
/// to answer "is this throughput number trustworthy?" without re-reading the
/// log: total attempts, successes, the failure breakdown by exception type,
/// and a derived <see cref="IsHealthy"/> flag against the configured
/// threshold.
/// <para>
/// The benchmark catches every non-cancellation exception inside the issuer
/// loop — silencing them would mask framework regressions (a wiring break
/// that fails every send would just report low EPS), so each is tallied and
/// surfaced. <see cref="FailureRate"/> is the dimensionless ratio over
/// attempts; the throughput numbers (EPS / window-end counter delta) come
/// from successful sends only, so a degraded run shows up as
/// <c>EPS reduced + FailureRate elevated</c> rather than a silent low EPS.
/// </para>
/// </summary>
public sealed record RunHealth(
    long Succeeded,
    long Failed,
    ImmutableSortedDictionary<string, long> FailuresByType)
{
    /// <summary>Default ceiling above which a point is flagged degraded.</summary>
    public const double DefaultFailureRateThreshold = 0.01;

    public static RunHealth Empty { get; } = new(
        Succeeded: 0,
        Failed: 0,
        FailuresByType: ImmutableSortedDictionary<string, long>.Empty);

    public long Attempted => Succeeded + Failed;

    public double FailureRate => Attempted > 0 ? (double)Failed / Attempted : 0;

    public bool IsHealthy => FailureRate <= DefaultFailureRateThreshold;

    /// <summary>
    /// Semicolon-joined <c>Type:count</c> summary for CSV embedding. Empty
    /// string when no failures were recorded so the CSV cell collapses to
    /// empty rather than carrying a misleading placeholder.
    /// </summary>
    public string RenderFailureTypes()
    {
        if (FailuresByType.Count == 0)
        {
            return string.Empty;
        }
        return string.Join(";", FailuresByType.Select(kvp =>
            string.Create(CultureInfo.InvariantCulture, $"{kvp.Key}:{kvp.Value}")));
    }
}
