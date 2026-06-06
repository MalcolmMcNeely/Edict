using Edict.Benchmarks.Throughput.Cluster;
using Edict.Benchmarks.Throughput.Measurement;
using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.Projections;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Substrate;

using Microsoft.Extensions.DependencyInjection;

namespace Edict.Benchmarks.Throughput.Saturation;

/// <summary>
/// Drives the saturation methodology — Events only, single parallelism
/// point, fire-and-forget producers, a single sum-of-counters read at
/// <c>t = window-end</c>. The substrate is brought up in the species-matched
/// saturation mode so Kafka consumers attach <c>AutoOffsetReset = Latest</c>;
/// Azurite is a no-op for the signal. EPS is the steady-state consumer ceiling
/// <c>min(producer_rate, consumer_rate)</c>; warmup contribution is subtracted via
/// a snapshot at warmup-end so the result is window-only.
/// <para>
/// The two species run the identical producer workload and differ only in how the
/// window-end count is read, so the EPS delta isolates the storage-commit cost:
/// the <see cref="ProjectionSpecies.List"/> pass sums the external
/// <c>BenchCounterRow</c> store-direct, the <see cref="ProjectionSpecies.State"/>
/// pass sums the in-grain <c>BenchCounterProjection</c> through the projection
/// reader (one grain hop per aggregate, at window-end only).
/// </para>
/// </summary>
public sealed class SaturationRunner
{
    const int FillerSizeBytes = 256;

    // Reused across every send and every issuer. Safe because Send(...) is
    // MessagePack-synchronous — the command is serialised before the call
    // returns and the buffer reference never escapes into asynchronous use.
    static readonly byte[] FillerBuffer = new byte[FillerSizeBytes];

    public Task<SaturationResults> RunAsync(
        ISubstrate substrate,
        ProjectionSpecies species,
        int parallelism,
        TimeSpan warmup,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(substrate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parallelism);

        var mode = species switch
        {
            ProjectionSpecies.List => SubstrateStartMode.SaturationList,
            ProjectionSpecies.State => SubstrateStartMode.SaturationState,
            _ => throw new ArgumentOutOfRangeException(nameof(species), species, "Unhandled projection species."),
        };

        return ClusterHarness.RunAsync(substrate, mode, async cluster =>
        {
            var sender = cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();
            var aggregatePool = Enumerable.Range(0, 1024)
                .Select(_ => Guid.NewGuid())
                .ToArray();

            // The producer workload is identical across species; only the
            // window-end read differs. The List pass reads the external counter
            // store directly (off-grain); the State pass reads the in-grain
            // counter through the projection reader (a grain hop per aggregate),
            // so the registered read surface is resolved per species.
            var sumCounters = BuildCounterSummer(species, cluster.Client.ServiceProvider, aggregatePool);

            // Warmup phase — producers fire at full rate so the consumer
            // reaches steady-state before the measurement window opens.
            // Failures during warmup don't bias the window-bracketed counter
            // delta, but they still flow through a tracker so the first-
            // occurrence stderr log fires early on a structurally broken run.
            await FireAndForgetAsync(
                sender, aggregatePool, parallelism, warmup,
                new IssuerOutcomeTracker($"{substrate.Name} Saturation {species} N={parallelism} warmup"),
                cancellationToken);

            // Subtracting the warmup-end snapshot from the window-end
            // snapshot leaves only window contributions in the count — the
            // honest steady-state delta the saturation EPS formula needs.
            var preWindow = await sumCounters(cancellationToken);

            var windowTracker = new IssuerOutcomeTracker($"{substrate.Name} Saturation {species} N={parallelism}");
            await FireAndForgetAsync(
                sender, aggregatePool, parallelism, window, windowTracker, cancellationToken);

            var postWindow = await sumCounters(cancellationToken);
            var windowEvents = postWindow - preWindow;
            var eps = window.TotalSeconds > 0
                ? windowEvents / window.TotalSeconds
                : 0;

            return new SaturationResults(
                Substrate: substrate.Name,
                Species: species,
                EventsPerSecond: eps,
                WindowSeconds: window.TotalSeconds,
                ProducerConcurrency: parallelism,
                AggregateCount: aggregatePool.Length,
                Health: windowTracker.Build());
        }, cancellationToken);
    }

    static Func<CancellationToken, Task<long>> BuildCounterSummer(
        ProjectionSpecies species,
        IServiceProvider serviceProvider,
        Guid[] aggregatePool)
    {
        switch (species)
        {
            case ProjectionSpecies.List:
                var counterRepository = serviceProvider.GetRequiredService<IEdictTableWriteStore<BenchCounterRow>>();
                return cancellationToken => SumStoreCountersAsync(counterRepository, aggregatePool, cancellationToken);
            case ProjectionSpecies.State:
                var counterReader = serviceProvider.GetRequiredService<IEdictProjectionReader<BenchCounterProjection>>();
                return cancellationToken => SumStateCountersAsync(counterReader, aggregatePool, cancellationToken);
            default:
                throw new ArgumentOutOfRangeException(nameof(species), species, "Unhandled projection species.");
        }
    }

    static async Task FireAndForgetAsync(
        IEdictSender sender,
        Guid[] aggregatePool,
        int parallelism,
        TimeSpan duration,
        IssuerOutcomeTracker tracker,
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
                    try
                    {
                        await sender.SendAsync(new BenchPublishCommand(aggregateId, Guid.NewGuid(), FillerBuffer));
                        tracker.RecordSuccess();
                    }
                    catch (OperationCanceledException)
                    {
                        // Window-end cancellation — not a failure.
                        break;
                    }
                    catch (Exception exception)
                    {
                        // Any other escape is counted: TimeoutException from
                        // Orleans' grain-call backpressure, OrleansException
                        // wrapping a storage-pool fault, or a framework
                        // regression. The window-bracketed counter delta
                        // measures consumer-side throughput; the producer-side
                        // failure breakdown lives in the RunHealth so the EPS
                        // is read against the offered load actually achieved.
                        tracker.RecordFailure(exception);
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

    static async Task<long> SumStoreCountersAsync(
        IEdictTableWriteStore<BenchCounterRow> repository,
        Guid[] aggregatePool,
        CancellationToken cancellationToken)
    {
        var total = 0L;
        foreach (var aggregateId in aggregatePool)
        {
            var row = await repository.GetAsync(
                aggregateId.ToString(),
                BenchCounterListProjectionBuilder.FixedRowKey,
                cancellationToken);
            if (row is not null)
            {
                total += row.Count;
            }
        }
        return total;
    }

    static async Task<long> SumStateCountersAsync(
        IEdictProjectionReader<BenchCounterProjection> reader,
        Guid[] aggregatePool,
        CancellationToken cancellationToken)
    {
        var total = 0L;
        foreach (var aggregateId in aggregatePool)
        {
            var read = await reader.ReadAsync(aggregateId, cancellationToken: cancellationToken);
            if (read.Value is not null)
            {
                total += read.Value.Count;
            }
        }
        return total;
    }
}
