using Edict.Benchmarks.Throughput.Saturation;
using Edict.Substrate.Azurite;

namespace Edict.Benchmarks.Throughput.Tests;

public sealed class SaturationRunnerTests
{
    [Theory]
    [InlineData(ProjectionSpecies.List)]
    [InlineData(ProjectionSpecies.State)]
    public async Task RunAsync_OnAzurite_ReturnsPopulatedSaturationResults(ProjectionSpecies species)
    {
        // Smoke shape against Azurite, both species. The State pass exercises the
        // in-grain reader sum path end-to-end (the List pass the store-direct sum).
        // Short warmup + window so the test finishes in seconds; production call
        // site uses 20 s + 30 s at N = 256. EPS must be strictly positive: a zero
        // means events were produced but the window-end count read nothing back,
        // which is exactly the failure a partition-key fold drift produces — the
        // List read addressing a partition the projection grain never wrote to.
        var substrate = new AzuriteSubstrate();
        var runner = new SaturationRunner();

        var result = await runner.RunAsync(
            substrate,
            species,
            parallelism: 4,
            warmup: TimeSpan.FromSeconds(2),
            window: TimeSpan.FromSeconds(3));

        Assert.Equal("azure", result.Substrate);
        Assert.Equal(species, result.Species);
        Assert.Equal(4, result.ProducerConcurrency);
        Assert.Equal(3, result.WindowSeconds);
        Assert.Equal(1024, result.AggregateCount);
        Assert.True(result.EventsPerSecond > 0, $"EventsPerSecond was {result.EventsPerSecond}");
    }
}
