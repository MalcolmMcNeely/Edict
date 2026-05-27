using Edict.Benchmarks.Throughput.Saturation;
using Edict.Substrate.Azurite;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class SaturationRunnerTests
{
    [Fact]
    public async Task RunAsync_OnAzurite_ReturnsPopulatedSaturationResults()
    {
        // Smoke shape against Azurite. Short warmup + window so the test
        // finishes in seconds; production call site uses 20 s + 30 s at
        // N = 256. The assertion is shape + non-negative EPS — exact
        // throughput is out of scope for a unit-style fact.
        var substrate = new AzuriteSubstrate();
        var runner = new SaturationRunner();

        var result = await runner.RunAsync(
            substrate,
            parallelism: 4,
            warmup: TimeSpan.FromSeconds(2),
            window: TimeSpan.FromSeconds(3));

        Assert.Equal("azure", result.Substrate);
        Assert.Equal(4, result.ProducerConcurrency);
        Assert.Equal(3, result.WindowSeconds);
        Assert.Equal(1024, result.AggregateCount);
        Assert.True(result.EventsPerSecond >= 0, $"EventsPerSecond was {result.EventsPerSecond}");
    }
}
