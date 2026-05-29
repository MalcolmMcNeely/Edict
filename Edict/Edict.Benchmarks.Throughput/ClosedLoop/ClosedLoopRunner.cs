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
/// <see cref="ClusterHarness"/> once and sweeps the parallelism axis.
/// For each (N, scenario) point the runner fans out N issuer
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
        CancellationToken cancellationToken = default)
    {
        var results = await RunCommandsSweepAsync(substrate, [parallelism], warmup, measurement, cancellationToken);
        return results[0];
    }

    public Task<IReadOnlyList<ThroughputResults>> RunCommandsSweepAsync(
        ISubstrate substrate,
        IReadOnlyList<int> parallelisms,
        TimeSpan warmup,
        TimeSpan measurement,
        CancellationToken cancellationToken = default) =>
        RunSweepAsync(
            substrate,
            serviceProvider => new CommandsScenario(serviceProvider.GetRequiredService<IEdictSender>()),
            parallelisms, warmup, measurement, cancellationToken);

    public Task<IReadOnlyList<ThroughputResults>> RunCommandsBaseTypedSweepAsync(
        ISubstrate substrate,
        IReadOnlyList<int> parallelisms,
        TimeSpan warmup,
        TimeSpan measurement,
        CancellationToken cancellationToken = default) =>
        RunSweepAsync(
            substrate,
            serviceProvider => new CommandsBaseTypedScenario(serviceProvider.GetRequiredService<IEdictSender>()),
            parallelisms, warmup, measurement, cancellationToken);

    public Task<IReadOnlyList<ThroughputResults>> RunEventsSweepAsync(
        ISubstrate substrate,
        IReadOnlyList<int> parallelisms,
        TimeSpan warmup,
        TimeSpan measurement,
        CancellationToken cancellationToken = default) =>
        RunSweepAsync(
            substrate,
            serviceProvider => new EventsScenario(
                serviceProvider.GetRequiredService<IEdictSender>(),
                serviceProvider.GetRequiredService<IEdictTableRepository<BenchEventRow>>()),
            parallelisms, warmup, measurement, cancellationToken);

    Task<IReadOnlyList<ThroughputResults>> RunSweepAsync(
        ISubstrate substrate,
        Func<IServiceProvider, IClosedLoopScenario> scenarioFactory,
        IReadOnlyList<int> parallelisms,
        TimeSpan warmup,
        TimeSpan measurement,
        CancellationToken cancellationToken)
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
                    substrate.Name, scenario, aggregatePool, parallelism, warmup, measurement, cancellationToken));
            }
            return (IReadOnlyList<ThroughputResults>)results;
        }, cancellationToken);
    }

    static async Task<ThroughputResults> RunSinglePointAsync(
        string substrateName,
        IClosedLoopScenario scenario,
        Guid[] aggregatePool,
        int parallelism,
        TimeSpan warmup,
        TimeSpan measurement,
        CancellationToken cancellationToken)
    {
        var warmupLabel = $"{substrateName} {scenario.Name} N={parallelism} warmup";
        var windowLabel = $"{substrateName} {scenario.Name} N={parallelism}";

        // Warmup — counts discarded, but failures still flow through a tracker
        // so first-failure stderr logs fire early. If the warmup is dying it
        // is wasted to burn the measurement window on a doomed point.
        await RunIssuersAsync(
            scenario, aggregatePool, parallelism, warmup, histograms: null,
            new IssuerOutcomeTracker(warmupLabel), cancellationToken);

        var histograms = LatencyCapture.CreateForIssuers(measurement, parallelism);
        var tracker = new IssuerOutcomeTracker(windowLabel);
        var (completed, elapsed) = await RunIssuersAsync(
            scenario, aggregatePool, parallelism, measurement, histograms, tracker, cancellationToken);

        return new ThroughputResults(
            Substrate: substrateName,
            Scenario: scenario.Name,
            Parallelism: parallelism,
            CompletedCount: completed,
            ElapsedMeasurement: elapsed,
            Latency: LatencyCapture.MergePercentiles(histograms),
            Health: tracker.Build())
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
        IssuerOutcomeTracker tracker,
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
                        // Window-end cancellation — not a failure.
                        break;
                    }
                    catch (Exception exception)
                    {
                        // Any other escape is recorded: TimeoutException from
                        // Orleans' default grain-call timeout, OrleansException
                        // wrapping a storage-pool fault, framework regression.
                        // Captured into the per-point tracker so the EPS row is
                        // read against an honest failure breakdown rather than
                        // a silent low number.
                        tracker.RecordFailure(exception);
                        index += parallelism;
                        continue;
                    }
                    histograms?[issuerIndex].Record(Stopwatch.GetTimestamp() - sendStarted);
                    tracker.RecordSuccess();
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
