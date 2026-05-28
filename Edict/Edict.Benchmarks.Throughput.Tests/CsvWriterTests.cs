using System.Collections.Immutable;

using Edict.Benchmarks.Throughput.ClosedLoop;
using Edict.Benchmarks.Throughput.Measurement;
using Edict.Benchmarks.Throughput.Output;

using static VerifyXunit.Verifier;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class CsvWriterTests
{
    [Fact]
    public Task Render_ShouldEmitLongFormatRowPerLatencySample_WithHealthColumns()
    {
        // Two sweep points carrying their own latency samples. Long-format
        // CSV: one row per sample, substrate + scenario + parallelism + the
        // group's EPS repeated so a reader pivoting on (substrate, scenario,
        // parallelism) gets one EPS per group while raw samples remain readable.
        // First point is OK; second point is degraded (succeeded + failed,
        // failure_types semicolon-joined) so the Verify snapshot covers both
        // shapes a reader will encounter.
        var results = new[]
        {
            new ThroughputResults(
                Substrate: "azure",
                Scenario: "Command acceptance",
                Parallelism: 2,
                CompletedCount: 300,
                ElapsedMeasurement: TimeSpan.FromSeconds(30),
                Latency: new LatencyResults(
                    P50: TimeSpan.FromMilliseconds(6.2),
                    P95: TimeSpan.FromMilliseconds(7.4),
                    P99: TimeSpan.FromMilliseconds(7.4)),
                Health: RunHealth.Empty with { Succeeded = 300 })
            {
                LatencySamples = new[]
                {
                    TimeSpan.FromMilliseconds(5.10),
                    TimeSpan.FromMilliseconds(6.20),
                    TimeSpan.FromMilliseconds(7.40),
                },
            },
            new ThroughputResults(
                Substrate: "azure",
                Scenario: "Command → Event delivery",
                Parallelism: 16,
                CompletedCount: 600,
                ElapsedMeasurement: TimeSpan.FromSeconds(30),
                Latency: new LatencyResults(
                    P50: TimeSpan.FromMilliseconds(5.0),
                    P95: TimeSpan.FromMilliseconds(5.5),
                    P99: TimeSpan.FromMilliseconds(5.5)),
                Health: new RunHealth(
                    Succeeded: 600,
                    Failed: 47,
                    FailuresByType: ImmutableSortedDictionary<string, long>.Empty
                        .Add("OrleansException", 32)
                        .Add("TimeoutException", 15)))
            {
                LatencySamples = new[]
                {
                    TimeSpan.FromMilliseconds(4.80),
                    TimeSpan.FromMilliseconds(5.50),
                },
            },
        };

        return Verify(CsvWriter.Render(results));
    }
}
