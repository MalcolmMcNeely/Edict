using System.Diagnostics;

using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.Sending;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

using Orleans.Hosting;
using Orleans.TestingHost;

namespace Edict.Benchmarks.Throughput;

/// <summary>
/// Orchestrates one (substrate, scenario, parallelism) measurement point.
/// TestCluster up → warmup → measure → teardown → return <see cref="ThroughputResults"/>.
/// Substrate-agnostic: every substrate-specific wiring decision flows through
/// the <see cref="ISubstrateRuntime"/> callbacks.
/// </summary>
public sealed class ThroughputRunner
{
    public async Task<ThroughputResults> RunCommandsAsync(
        ISubstrate substrate,
        int parallelism,
        TimeSpan warmup,
        TimeSpan measurement,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(substrate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parallelism);

        await using var runtime = await substrate.StartAsync(ct);

        ActiveRuntime.Current = runtime;
        try
        {
            var clusterBuilder = new TestClusterBuilder();
            clusterBuilder.AddSiloBuilderConfigurator<ActiveRuntime.SiloConfigurator>();
            clusterBuilder.AddClientBuilderConfigurator<ActiveRuntime.ClientConfigurator>();
            var cluster = clusterBuilder.Build();
            await cluster.DeployAsync();
            try
            {
                var sender = cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();
                var aggregatePool = Enumerable.Range(0, 1024)
                    .Select(_ => Guid.NewGuid())
                    .ToArray();

                // Warmup — discarded.
                await RunIssuersAsync(sender, aggregatePool, parallelism, warmup, histograms: null, ct);

                // Measurement.
                var histograms = new LatencyHistogram[parallelism];
                var capacityPerIssuer = EstimateCapacity(measurement, parallelism);
                for (var i = 0; i < parallelism; i++)
                {
                    histograms[i] = new LatencyHistogram(capacityPerIssuer);
                }
                var (completed, elapsed) = await RunIssuersAsync(
                    sender, aggregatePool, parallelism, measurement, histograms, ct);

                var latency = MergePercentiles(histograms);

                return new ThroughputResults(
                    Substrate: substrate.Name,
                    Scenario: "Commands",
                    Parallelism: parallelism,
                    CompletedCount: completed,
                    ElapsedMeasurement: elapsed,
                    Latency: latency);
            }
            finally
            {
                await cluster.DisposeAsync();
            }
        }
        finally
        {
            ActiveRuntime.Current = null;
        }
    }

    static int EstimateCapacity(TimeSpan window, int parallelism)
    {
        // Rough upper bound — assume up to 10k EPS per issuer; pre-size accordingly
        // with a 2x safety factor. Drops on overflow are silent in LatencyHistogram.
        var perIssuer = (int)(window.TotalSeconds * 10_000 * 2);
        return Math.Max(1024, perIssuer / Math.Max(1, parallelism) + 1024);
    }

    static async Task<(long Completed, TimeSpan Elapsed)> RunIssuersAsync(
        IEdictSender sender,
        Guid[] aggregatePool,
        int parallelism,
        TimeSpan window,
        LatencyHistogram[]? histograms,
        CancellationToken outerCt)
    {
        using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        windowCts.CancelAfter(window);

        var completed = new long[parallelism];
        var stopwatch = Stopwatch.StartNew();
        var tasks = new Task[parallelism];

        for (var i = 0; i < parallelism; i++)
        {
            var issuerIndex = i;
            tasks[i] = Task.Run(async () =>
            {
                var localCompleted = 0L;
                var index = issuerIndex;
                while (!windowCts.IsCancellationRequested)
                {
                    var aggregateId = aggregatePool[(int)(((uint)index) % aggregatePool.Length)];
                    var sendStarted = Stopwatch.GetTimestamp();
                    try
                    {
                        await sender.Send(new BenchIncrementCommand(aggregateId));
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    var delta = Stopwatch.GetTimestamp() - sendStarted;
                    histograms?[issuerIndex].Record(delta);
                    localCompleted++;
                    index += parallelism;
                }
                completed[issuerIndex] = localCompleted;
            });
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
        stopwatch.Stop();

        return (completed.Sum(), stopwatch.Elapsed);
    }

    static LatencyResults MergePercentiles(LatencyHistogram[] histograms)
    {
        var total = histograms.Sum(h => h.Count);
        if (total == 0)
        {
            return new LatencyResults(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        }
        var merged = new LatencyHistogram(total);
        foreach (var histogram in histograms)
        {
            foreach (var sample in histogram.AsReadOnlySpan())
            {
                merged.Record(sample);
            }
        }
        return merged.Compute();
    }

    static class ActiveRuntime
    {
        // Cross-process bridge for TestClusterBuilder's parameterless-ctor
        // configurators. The runner serialises substrate runs so a static is
        // safe — one cluster up at a time.
        public static ISubstrateRuntime? Current { get; set; }

        public sealed class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                var runtime = Current ?? throw new InvalidOperationException(
                    "ActiveRuntime.Current was null when the silo configurator ran.");
                runtime.ConfigureSilo(siloBuilder);
            }
        }

        public sealed class ClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            {
                var runtime = Current ?? throw new InvalidOperationException(
                    "ActiveRuntime.Current was null when the client configurator ran.");
                runtime.ConfigureClient(clientBuilder);
            }
        }
    }
}
