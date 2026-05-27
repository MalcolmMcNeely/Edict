using Edict.Benchmarks.Throughput;

using static VerifyXunit.Verifier;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class SaturationCsvWriterTests
{
    [Fact]
    public Task Render_ShouldEmitRowPerSaturationResult()
    {
        var results = new[]
        {
            new SaturationResults(
                Substrate: "azure",
                EventsPerSecond: 73.4,
                WindowSeconds: 30,
                ProducerConcurrency: 256,
                AggregateCount: 1024),
            new SaturationResults(
                Substrate: "kafkapostgres",
                EventsPerSecond: 412.6,
                WindowSeconds: 30,
                ProducerConcurrency: 256,
                AggregateCount: 1024),
        };

        return Verify(SaturationCsvWriter.Render(results));
    }

    [Fact]
    public void Render_ShouldEmitHeaderOnly_ForEmptyResults()
    {
        var output = SaturationCsvWriter.Render([]);

        Assert.Equal(
            "substrate,events_per_second,window_seconds,producer_concurrency,aggregate_count" + Environment.NewLine,
            output);
    }
}
