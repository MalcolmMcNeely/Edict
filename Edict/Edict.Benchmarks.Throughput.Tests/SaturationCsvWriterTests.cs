using System.Collections.Immutable;

using Edict.Benchmarks.Throughput.Measurement;
using Edict.Benchmarks.Throughput.Output;
using Edict.Benchmarks.Throughput.Saturation;

using static VerifyXunit.Verifier;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class SaturationCsvWriterTests
{
    [Fact]
    public Task Render_ShouldEmitRowPerSaturationResult_WithHealthColumns()
    {
        // One healthy row + one degraded row so the snapshot covers both
        // shapes: empty failure_types cell on the OK path, semicolon-joined
        // Type:count breakdown on the degraded path.
        var results = new[]
        {
            new SaturationResults(
                Substrate: "azure",
                EventsPerSecond: 73.4,
                WindowSeconds: 30,
                ProducerConcurrency: 256,
                AggregateCount: 1024,
                Health: RunHealth.Empty with { Succeeded = 8400 }),
            new SaturationResults(
                Substrate: "kafkapostgres",
                EventsPerSecond: 412.6,
                WindowSeconds: 30,
                ProducerConcurrency: 256,
                AggregateCount: 1024,
                Health: new RunHealth(
                    Succeeded: 19320,
                    Failed: 4280,
                    FailuresByType: ImmutableSortedDictionary<string, long>.Empty
                        .Add("EdictPostgresStorageException", 3100)
                        .Add("TimeoutException", 1180))),
        };

        return Verify(SaturationCsvWriter.Render(results));
    }

    [Fact]
    public void Render_ShouldEmitHeaderOnly_ForEmptyResults()
    {
        var output = SaturationCsvWriter.Render([]);

        Assert.Equal(
            "substrate,events_per_second,window_seconds,producer_concurrency,aggregate_count,succeeded,failed,failure_rate,failure_types" + Environment.NewLine,
            output);
    }
}
