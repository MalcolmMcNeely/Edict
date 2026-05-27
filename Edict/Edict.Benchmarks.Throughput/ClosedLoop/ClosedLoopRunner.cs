using System.Diagnostics;

using Edict.Benchmarks.Throughput.Cluster;
using Edict.Benchmarks.Throughput.Measurement;
using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Substrate;

using Microsoft.Extensions.DependencyInjection;

namespace Edict.Benchmarks.Throughput.ClosedLoop;

/// <summary>
/// Drives the closed-loop methodology: per-substrate, brings up the
/// <see cref="ClusterHarness"/> once and sweeps the parallelism axis
/// (issue #126). For each (N, scenario) point the runner fans out N issuer
/// tasks calling <see cref="IClosedLoopScenario.IssueOnceAsync"/> in a
/// tight loop, captures per-send latency into a per-issuer histogram, and
/// emits a <see cref="ThroughputResults"/> with downsampled raw samples
/// for the CSV writer.
/// </summary>
public sealed class ClosedLoopRunner
{
    const int FillerSizeBytes = 256;

    // Reused across every issue and every issuer. Safe because the scenarios
    // hand the buffer to MessagePack-backed Send(...), which serialises the
    // command synchronously before returning — the reference never escapes
    // into asynchronous use, and the bench never mutates the buffer.
    static readonly byte[] FillerBuffer = new byte[FillerSizeBytes];

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
        RunSweepAsync(
            substrate,
            sp => new CommandsScenario(sp.GetRequiredService<IEdictSender>()),
            parallelisms, warmup, measurement, ct);

    public Task<IReadOnlyList<ThroughputResults>> RunRaiseOnlySweepAsync(
        ISubstrate substrate,
        IReadOnlyList<int> parallelisms,
        TimeSpan warmup,
        TimeSpan measurement,
        CancellationToken ct = default) =>
        RunSweepAsync(
            substrate,
            sp => new RaiseOnlyScenario(sp.GetRequiredService<IEdictSender>()),
            parallelisms, warmup, measurement, ct);

    public Task<IReadOnlyList<ThroughputResults>> RunEventsSweepAsync(
        ISubstrate substrate,
        IReadOnlyList<int> parallelisms,
        TimeSpan warmup,
        TimeSpan measurement,
        CancellationToken ct = default) =>
        RunSweepAsync(
            substrate,
            sp => new EventsScenario(
                sp.GetRequiredService<IEdictSender>(),
                sp.GetRequiredService<IEdictTableRepository<BenchEventRow>>()),
            parallelisms, warmup, measurement, ct);

    Task<IReadOnlyList<ThroughputResults>> RunSweepAsync(
        ISubstrate substrate,
        Func<IServiceProvider, IClosedLoopScenario> scenarioFactory,
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

        return ClusterHarness.RunAsync(substrate, SubstrateStartMode.ClosedLoop, async cluster =>
        {
            var scenario = scenarioFactory(cluster.Client.ServiceProvider);
            var aggregatePool = Enumerable.Range(0, 1024)
                .Select(_ => Guid.NewGuid())
                .ToArray();

            var results = new List<ThroughputResults>(parallelisms.Count);
            foreach (var parallelism in parallelisms)
            {
                results.Add(await RunSinglePointAsync(
                    substrate.Name, scenario, aggregatePool, parallelism, warmup, measurement, ct));
            }
            return (IReadOnlyList<ThroughputResults>)results;
        }, ct);
    }

    static async Task<ThroughputResults> RunSinglePointAsync(
        string substrateName,
        IClosedLoopScenario scenario,
        Guid[] aggregatePool,
        int parallelism,
        TimeSpan warmup,
        TimeSpan measurement,
        CancellationToken ct)
    {
        // Warmup — discarded.
        await RunIssuersAsync(scenario, aggregatePool, parallelism, warmup, histograms: null, ct);

        var histograms = LatencyCapture.CreateForIssuers(measurement, parallelism);
        var (completed, elapsed) = await RunIssuersAsync(
            scenario, aggregatePool, parallelism, measurement, histograms, ct);

        return new ThroughputResults(
            Substrate: substrateName,
            Scenario: scenario.Name,
            Parallelism: parallelism,
            CompletedCount: completed,
            ElapsedMeasurement: elapsed,
            Latency: LatencyCapture.MergePercentiles(histograms))
        {
            LatencySamples = LatencyCapture.DownsampleSamples(histograms),
        };
    }

    static async Task<(long Completed, TimeSpan Elapsed)> RunIssuersAsync(
        IClosedLoopScenario scenario,
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
                        await scenario.IssueOnceAsync(aggregateId, FillerBuffer, windowCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (TimeoutException)
                    {
                        // Saturation backpressure: the silo did not respond within
                        // Orleans' default 30 s grain-call timeout. Skip this send so
                        // one slow grain call doesn't crash the whole sweep.
                        index += parallelism;
                        continue;
                    }
                    histograms?[issuerIndex].Record(Stopwatch.GetTimestamp() - sendStarted);
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
}
