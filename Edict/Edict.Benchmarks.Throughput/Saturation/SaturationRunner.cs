using Edict.Benchmarks.Throughput.Cluster;
using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Substrate;

using Microsoft.Extensions.DependencyInjection;

namespace Edict.Benchmarks.Throughput.Saturation;

/// <summary>
/// Drives the saturation methodology — Events only, single parallelism
/// point, fire-and-forget producers, a single sum-of-counters read at
/// <c>t = window-end</c>. The substrate is brought up in
/// <see cref="SubstrateStartMode.Saturation"/> so Kafka consumers attach
/// <c>AutoOffsetReset = Latest</c>; Azurite is a no-op for the signal. EPS
/// is the steady-state consumer ceiling
/// <c>min(producer_rate, consumer_rate)</c>; warmup contribution is
/// subtracted via a snapshot at warmup-end so the result is window-only.
/// </summary>
public sealed class SaturationRunner
{
    const int FillerSizeBytes = 256;

    public Task<SaturationResults> RunAsync(
        ISubstrate substrate,
        int parallelism,
        TimeSpan warmup,
        TimeSpan window,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(substrate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parallelism);

        return ClusterHarness.RunAsync(substrate, SubstrateStartMode.Saturation, async cluster =>
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
        }, ct);
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
}
