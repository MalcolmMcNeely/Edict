using System.Diagnostics;

using Azure.Data.Tables;

using Edict.Azure.TableStorage;
using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Substrate;
using Edict.Substrate.Azurite;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

using Orleans.Hosting;
using Orleans.TestingHost;

namespace Edict.Benchmarks.Throughput;

/// <summary>
/// Orchestrates measurement: per-substrate it brings up Azurite + a TestCluster
/// once, then sweeps the parallelism axis (issue #126), returning one
/// <see cref="ThroughputResults"/> per (substrate, scenario, parallelism) point
/// with raw latency samples attached for the CSV writer.
/// </summary>
public sealed class ThroughputRunner
{
    const int FillerSizeBytes = 256;

    /// <summary>
    /// Single-point convenience: starts the substrate, runs one Commands point,
    /// tears down. Used by ad-hoc explorations; the publishable sweep uses
    /// <see cref="RunCommandsSweepAsync"/> so the substrate stays up across N.
    /// </summary>
    public async Task<ThroughputResults> RunCommandsAsync(
        ISubstrate substrate,
        int parallelism,
        TimeSpan warmup,
        TimeSpan measurement,
        CancellationToken ct = default)
    {
        var results = await RunCommandsSweepAsync(substrate, [parallelism], warmup, measurement, ct);
        return results[0];
    }

    public Task<IReadOnlyList<ThroughputResults>> RunCommandsSweepAsync(
        ISubstrate substrate,
        IReadOnlyList<int> parallelisms,
        TimeSpan warmup,
        TimeSpan measurement,
        CancellationToken ct = default) =>
        RunSweepAsync(substrate, Scenario.Commands, parallelisms, warmup, measurement, ct);

    public Task<IReadOnlyList<ThroughputResults>> RunEventsSweepAsync(
        ISubstrate substrate,
        IReadOnlyList<int> parallelisms,
        TimeSpan warmup,
        TimeSpan measurement,
        CancellationToken ct = default) =>
        RunSweepAsync(substrate, Scenario.Events, parallelisms, warmup, measurement, ct);

    public Task<IReadOnlyList<ThroughputResults>> RunRaiseOnlySweepAsync(
        ISubstrate substrate,
        IReadOnlyList<int> parallelisms,
        TimeSpan warmup,
        TimeSpan measurement,
        CancellationToken ct = default) =>
        RunSweepAsync(substrate, Scenario.RaiseOnly, parallelisms, warmup, measurement, ct);

    async Task<IReadOnlyList<ThroughputResults>> RunSweepAsync(
        ISubstrate substrate,
        Scenario scenario,
        IReadOnlyList<int> parallelisms,
        TimeSpan warmup,
        TimeSpan measurement,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(substrate);
        ArgumentNullException.ThrowIfNull(parallelisms);
        if (parallelisms.Count == 0)
        {
            throw new ArgumentException("At least one parallelism point required.", nameof(parallelisms));
        }
        foreach (var n in parallelisms)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(n);
        }

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
                var eventRowRepository = scenario == Scenario.Events
                    ? cluster.Client.ServiceProvider.GetRequiredService<IEdictTableRepository<BenchEventRow>>()
                    : null;
                var aggregatePool = Enumerable.Range(0, 1024)
                    .Select(_ => Guid.NewGuid())
                    .ToArray();

                var results = new List<ThroughputResults>(parallelisms.Count);
                foreach (var parallelism in parallelisms)
                {
                    results.Add(await RunSinglePointAsync(
                        substrate.Name, scenario, sender, eventRowRepository,
                        aggregatePool, parallelism, warmup, measurement, ct));
                }
                return results;
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

    static async Task<ThroughputResults> RunSinglePointAsync(
        string substrateName,
        Scenario scenario,
        IEdictSender sender,
        IEdictTableRepository<BenchEventRow>? eventRowRepository,
        Guid[] aggregatePool,
        int parallelism,
        TimeSpan warmup,
        TimeSpan measurement,
        CancellationToken ct)
    {
        // Warmup — discarded.
        await RunIssuersAsync(
            scenario, sender, eventRowRepository, aggregatePool, parallelism, warmup, histograms: null, ct);

        // Measurement.
        var histograms = new LatencyHistogram[parallelism];
        var capacityPerIssuer = EstimateCapacity(measurement, parallelism);
        for (var i = 0; i < parallelism; i++)
        {
            histograms[i] = new LatencyHistogram(capacityPerIssuer);
        }
        var (completed, elapsed) = await RunIssuersAsync(
            scenario, sender, eventRowRepository, aggregatePool, parallelism, measurement, histograms, ct);

        var latency = MergePercentiles(histograms);
        var samples = DownsampleSamples(histograms);

        return new ThroughputResults(
            Substrate: substrateName,
            Scenario: scenario.ToString(),
            Parallelism: parallelism,
            CompletedCount: completed,
            ElapsedMeasurement: elapsed,
            Latency: latency)
        {
            LatencySamples = samples,
        };
    }

    static int EstimateCapacity(TimeSpan window, int parallelism)
    {
        // Rough upper bound — assume up to 10k EPS per issuer; pre-size accordingly
        // with a 2x safety factor. Drops on overflow are silent in LatencyHistogram.
        var perIssuer = (int)(window.TotalSeconds * 10_000 * 2);
        return Math.Max(1024, perIssuer / Math.Max(1, parallelism) + 1024);
    }

    static async Task<(long Completed, TimeSpan Elapsed)> RunIssuersAsync(
        Scenario scenario,
        IEdictSender sender,
        IEdictTableRepository<BenchEventRow>? eventRowRepository,
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
        var pollInterval = TimeSpan.FromMilliseconds(5);

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
                    var filler = new byte[FillerSizeBytes];
                    var sendStarted = Stopwatch.GetTimestamp();
                    try
                    {
                        if (scenario == Scenario.Commands)
                        {
                            await sender.Send(new BenchIncrementCommand(aggregateId, filler));
                        }
                        else if (scenario == Scenario.RaiseOnly)
                        {
                            await sender.Send(new BenchPublishCommand(aggregateId, Guid.NewGuid(), filler));
                        }
                        else
                        {
                            var correlationId = Guid.NewGuid();
                            await sender.Send(new BenchPublishCommand(aggregateId, correlationId, filler));
                            await WaitForEventRowAsync(
                                eventRowRepository!, aggregateId, correlationId.ToString("D"), pollInterval, windowCts.Token);
                        }
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

    static async Task WaitForEventRowAsync(
        IEdictTableRepository<BenchEventRow> repository,
        Guid aggregateId,
        string rowKey,
        TimeSpan pollInterval,
        CancellationToken ct)
    {
        // Substrate-neutral completion signal — point-get against the
        // projection's pk/rk pair, polled at ~5ms. Cost scales with EPS
        // (one completion = one wake), not against it.
        var partitionKey = aggregateId.ToString();
        while (!ct.IsCancellationRequested)
        {
            var row = await repository.GetAsync(partitionKey, rowKey, ct);
            if (row is not null)
            {
                return;
            }
            try
            {
                await Task.Delay(pollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
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

    const int MaxCsvSamplesPerPoint = 10_000;

    static IReadOnlyList<TimeSpan> DownsampleSamples(LatencyHistogram[] histograms)
    {
        var total = histograms.Sum(h => h.Count);
        if (total == 0)
        {
            return [];
        }
        // Stride-pick across the concatenated raw samples so the CSV stays
        // bounded (~10k rows per point) but is dense enough to re-derive
        // percentiles or plot the curve.
        var stride = Math.Max(1, total / MaxCsvSamplesPerPoint);
        var output = new List<TimeSpan>(Math.Min(total, MaxCsvSamplesPerPoint));
        var index = 0;
        foreach (var histogram in histograms)
        {
            var span = histogram.AsReadOnlySpan();
            for (var i = 0; i < span.Length; i++)
            {
                if (index % stride == 0)
                {
                    output.Add(TimeSpan.FromSeconds((double)span[i] / Stopwatch.Frequency));
                }
                index++;
            }
        }
        return output;
    }

    enum Scenario
    {
        Commands,
        RaiseOnly,
        Events,
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
                // Workload-specific repository — the substrate library stays
                // workload-free, so the harness registers its own row type
                // here against the TableServiceClient the substrate published.
                clientBuilder.Services.AddSingleton<IEdictTableRepository<BenchEventRow>>(sp =>
                    new AzureTableRepository<BenchEventRow>(
                        sp.GetRequiredService<TableServiceClient>(),
                        BenchProjectionBuilder.TableNameLiteral));
            }
        }
    }
}
