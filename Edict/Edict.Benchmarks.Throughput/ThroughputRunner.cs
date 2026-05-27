using System.Diagnostics;

using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Substrate;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

    /// <summary>
    /// Saturation pass — Events only, single parallelism point, fire-and-forget
    /// producers, single sum-of-counters read at <c>t = window-end</c>. The
    /// substrate is brought up in <see cref="SubstrateStartMode.Saturation"/>
    /// so Kafka consumers attach <c>AutoOffsetReset = Latest</c>; Azurite is a
    /// no-op for the signal. EPS is the steady-state consumer ceiling
    /// <c>min(producer_rate, consumer_rate)</c>; warmup contribution is
    /// subtracted via a snapshot at warmup-end so the result is window-only.
    /// </summary>
    public async Task<SaturationResults> RunSaturationAsync(
        ISubstrate substrate,
        int parallelism,
        TimeSpan warmup,
        TimeSpan window,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(substrate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parallelism);

        await using var runtime = await substrate.StartAsync(ct, SubstrateStartMode.Saturation);

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
                var counterRepository = cluster.Client.ServiceProvider
                    .GetRequiredService<IEdictTableRepository<BenchCounterRow>>();
                var aggregatePool = Enumerable.Range(0, 1024)
                    .Select(_ => Guid.NewGuid())
                    .ToArray();

                // Warmup phase — producers fire at full rate so the consumer
                // reaches steady-state before the measurement window opens.
                await FireAndForgetAsync(sender, aggregatePool, parallelism, warmup, ct);

                // Subtracting the warmup-end snapshot from the window-end
                // snapshot leaves only window contributions in the count — the
                // honest steady-state delta the saturation EPS formula needs.
                var preWindow = await SumCountersAsync(counterRepository, aggregatePool, ct);

                await FireAndForgetAsync(sender, aggregatePool, parallelism, window, ct);

                var postWindow = await SumCountersAsync(counterRepository, aggregatePool, ct);
                var windowEvents = postWindow - preWindow;
                var eps = window.TotalSeconds > 0
                    ? windowEvents / window.TotalSeconds
                    : 0;

                return new SaturationResults(
                    Substrate: substrate.Name,
                    EventsPerSecond: eps,
                    WindowSeconds: window.TotalSeconds,
                    ProducerConcurrency: parallelism,
                    AggregateCount: aggregatePool.Length);
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

    static async Task FireAndForgetAsync(
        IEdictSender sender,
        Guid[] aggregatePool,
        int parallelism,
        TimeSpan duration,
        CancellationToken outerCt)
    {
        using var windowCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        windowCts.CancelAfter(duration);

        var tasks = new Task[parallelism];
        for (var i = 0; i < parallelism; i++)
        {
            var issuerIndex = i;
            tasks[i] = Task.Run(async () =>
            {
                var index = issuerIndex;
                while (!windowCts.IsCancellationRequested)
                {
                    var aggregateId = aggregatePool[(int)(((uint)index) % aggregatePool.Length)];
                    var filler = new byte[FillerSizeBytes];
                    try
                    {
                        await sender.Send(new BenchPublishCommand(aggregateId, Guid.NewGuid(), filler));
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (TimeoutException)
                    {
                        // Saturation backpressure: skip slow sends rather than crash
                        // the run. The sum-of-counters read at window-end measures
                        // what the consumer actually drained, not issuer attempts.
                    }
                    index += parallelism;
                }
            });
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
    }

    static async Task<long> SumCountersAsync(
        IEdictTableRepository<BenchCounterRow> repository,
        Guid[] aggregatePool,
        CancellationToken ct)
    {
        var total = 0L;
        foreach (var aggregateId in aggregatePool)
        {
            var row = await repository.GetAsync(
                aggregateId.ToString(),
                BenchCounterProjectionBuilder.FixedRowKey,
                ct);
            if (row is not null)
            {
                total += row.Count;
            }
        }
        return total;
    }

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
                    catch (TimeoutException)
                    {
                        // Saturation backpressure: the silo did not respond within
                        // Orleans' default 30s grain-call timeout. Skip this send so
                        // one slow grain call doesn't crash the whole sweep.
                        index += parallelism;
                        continue;
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
                // here. The runtime's CreateRowRepository<T> seam picks the
                // substrate-correct repo (AzureTableRepository on Azurite,
                // PostgresTableRepository on Kafka+Postgres) without the
                // harness branching on substrate kind.
                clientBuilder.Services.AddSingleton<IEdictTableRepository<BenchEventRow>>(sp =>
                    runtime.CreateRowRepository<BenchEventRow>(sp, BenchProjectionBuilder.TableNameLiteral));
                clientBuilder.Services.AddSingleton<IEdictTableRepository<BenchCounterRow>>(sp =>
                    runtime.CreateRowRepository<BenchCounterRow>(sp, BenchCounterProjectionBuilder.TableNameLiteral));
            }
        }
    }
}
