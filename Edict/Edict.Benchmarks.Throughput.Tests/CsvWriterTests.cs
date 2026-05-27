using Edict.Benchmarks.Throughput.ClosedLoop;
using Edict.Benchmarks.Throughput.Measurement;
using Edict.Benchmarks.Throughput.Output;

using static VerifyXunit.Verifier;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class CsvWriterTests
{
    [Fact]
    public Task Render_ShouldEmitLongFormatRowPerLatencySample()
    {
        // Two sweep points carrying their own latency samples. Long-format
        // CSV: one row per sample, substrate + scenario + parallelism repeated.
        var results = new[]
        {
            new ThroughputResults(
                Substrate: "azure",
                Scenario: "Commands",
                Parallelism: 1,
                CompletedCount: 3,
                ElapsedMeasurement: TimeSpan.FromSeconds(30),
                Latency: new LatencyResults(
                    P50: TimeSpan.FromMilliseconds(6.2),
                    P95: TimeSpan.FromMilliseconds(7.4),
                    P99: TimeSpan.FromMilliseconds(7.4)))
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
                Scenario: "Commands",
                Parallelism: 4,
                CompletedCount: 2,
                ElapsedMeasurement: TimeSpan.FromSeconds(30),
                Latency: new LatencyResults(
                    P50: TimeSpan.FromMilliseconds(5.0),
                    P95: TimeSpan.FromMilliseconds(5.5),
                    P99: TimeSpan.FromMilliseconds(5.5)))
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
