using Edict.Benchmarks.Throughput;

using static VerifyXunit.Verifier;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class MarkdownWriterTests
{
    static readonly DateTimeOffset FixedRunDate =
        new(2026, 5, 26, 12, 0, 0, TimeSpan.Zero);

    static readonly RunMetadata FixedMetadata = new(
        MachineClass: "Linux 5.15 / 16 cores",
        DotnetVersion: "10.0.8",
        GitSha: "64fd1a7");

    [Fact]
    public Task Render_ShouldIncludeMachineNetVersionRunDateAndGitShaInHeader()
    {
        var results = new[]
        {
            new ThroughputResults(
                Substrate: "azure",
                Scenario: "Commands",
                Parallelism: 4,
                CompletedCount: 6_560,
                ElapsedMeasurement: TimeSpan.FromSeconds(10),
                Latency: new LatencyResults(
                    P50: TimeSpan.FromMilliseconds(5.77),
                    P95: TimeSpan.FromMilliseconds(9.09),
                    P99: TimeSpan.FromMilliseconds(11.29))),
        };

        return Verify(MarkdownWriter.Render(results, FixedRunDate, FixedMetadata));
    }

    [Fact]
    public Task Render_ShouldEmitPeakEpsHeadlinePerSubstrate_ForFullSweep()
    {
        // Two substrates, five sweep points each. Peak EPS is at different N
        // for each substrate so the headline can't just pick the last row.
        var results = new[]
        {
            // azure — peak at N=64 (24_000 EPS)
            SweepPoint("azure", 1, 1_200, 8.0, 14.0, 20.0),
            SweepPoint("azure", 4, 4_500, 6.5, 11.0, 16.0),
            SweepPoint("azure", 16, 16_000, 5.0, 9.5, 13.0),
            SweepPoint("azure", 64, 24_000, 5.2, 10.0, 18.0),
            SweepPoint("azure", 256, 18_000, 8.0, 22.0, 45.0),
            // kafka — peak at N=256 (52_000 EPS); proves per-substrate split
            SweepPoint("kafka", 1, 4_000, 2.0, 4.0, 6.5),
            SweepPoint("kafka", 4, 14_000, 2.2, 4.5, 7.0),
            SweepPoint("kafka", 16, 32_000, 2.5, 5.0, 9.0),
            SweepPoint("kafka", 64, 46_000, 3.0, 6.0, 12.0),
            SweepPoint("kafka", 256, 52_000, 4.5, 9.0, 20.0),
        };

        return Verify(MarkdownWriter.Render(results, FixedRunDate, FixedMetadata));
    }

    static ThroughputResults SweepPoint(
        string substrate, int n, double eps,
        double p50Ms, double p95Ms, double p99Ms)
    {
        var window = TimeSpan.FromSeconds(30);
        var completed = (long)(eps * window.TotalSeconds);
        return new ThroughputResults(
            Substrate: substrate,
            Scenario: "Commands",
            Parallelism: n,
            CompletedCount: completed,
            ElapsedMeasurement: window,
            Latency: new LatencyResults(
                P50: TimeSpan.FromMilliseconds(p50Ms),
                P95: TimeSpan.FromMilliseconds(p95Ms),
                P99: TimeSpan.FromMilliseconds(p99Ms)));
    }
}
