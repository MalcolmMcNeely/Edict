using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.Sending;
using Edict.Substrate;
using Edict.Substrate.KafkaPostgres;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.TestingHost;

using Xunit.Abstractions;

namespace Edict.Benchmarks.Throughput.Tests;

/// <summary>
/// Diagnostic probe — falsify or confirm the Npgsql connection-pool-pressure
/// hypothesis behind the kafkapostgres Commands-curve plateau
/// (1481 EPS @ N=64 → 1336 EPS @ N=256, see docs/benchmarks/throughput.md).
/// Subscribes a raw <see cref="NpgsqlPoolListener"/> to the <c>"Npgsql"</c>
/// meter so we read the connection pool's own telemetry rather than infer
/// from EPS, then runs the Commands scenario at N ∈ {64, 256} against the
/// real Kafka + Postgres substrate.
/// <para>
/// Verdict thresholds: pool-acquire queueing (<c>pending_requests &gt; 0</c>)
/// sustained for &gt;1 s OR new-connection establishment p99
/// (<c>create_time</c>, the closest Npgsql 10 signal to a wait_time
/// instrument) &gt; 10 ms on any sweep point → pressure observed; the
/// prior "lean on Npgsql pooling" position against an
/// <c>NpgsqlDataSource</c> singleton is then stale. Otherwise the plateau is
/// something else (grain-turn serialisation, row contention, network floor)
/// and the singleton refactor stays unmotivated.
/// </para>
/// </summary>
public sealed class NpgsqlPoolPressureProbeTests
{
    readonly ITestOutputHelper _output;

    public NpgsqlPoolPressureProbeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Diagnostic probe — un-skip manually to run against live Kafka+Postgres containers (~1.5 min). Captures Npgsql connection-pool metrics during a Commands sweep at N ∈ {64, 256}; verdict is printed to ITestOutputHelper.")]
    public async Task Probe_CommandsSweep_OnKafkaPostgres_ReportsPoolPressureVerdict()
    {
        var warmup = TimeSpan.FromSeconds(3);
        var measurement = TimeSpan.FromSeconds(15);
        int[] parallelisms = [64, 256];

        using var listener = new NpgsqlPoolListener();
        listener.Start();

        var substrate = new KafkaPostgresSubstrate();
        await using var runtime = await substrate.StartAsync(CancellationToken.None, SubstrateStartMode.ClosedLoop);

        ActiveProbeRuntime.Current = runtime;
        try
        {
            var builder = new TestClusterBuilder();
            builder.AddSiloBuilderConfigurator<ActiveProbeRuntime.SiloConfigurator>();
            builder.AddClientBuilderConfigurator<ActiveProbeRuntime.ClientConfigurator>();
            var cluster = builder.Build();
            await cluster.DeployAsync();
            try
            {
                var sender = cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();
                var aggregatePool = Enumerable.Range(0, 1024).Select(_ => Guid.NewGuid()).ToArray();

                var verdicts = new List<(int N, NpgsqlPoolListener.Snapshot Snapshot, bool Pressure)>();
                foreach (var n in parallelisms)
                {
                    listener.Reset();

                    // Warmup so the pool reaches steady state before measurement
                    // — peaks captured during warmup would conflate pool warmup
                    // (initial fill from 0 → N connections) with sweep-point
                    // pressure.
                    await DriveIssuersAsync(listener, sender, aggregatePool, n, warmup);
                    listener.Reset();

                    await DriveIssuersAsync(listener, sender, aggregatePool, n, measurement);

                    // Final observable sample after the workload settles so the
                    // snapshot reflects the most recent in-pool state.
                    listener.RecordObservables();
                    var snapshot = listener.Capture();

                    var sustainedExceedsOneSecond = snapshot.LongestSustainedPending > TimeSpan.FromSeconds(1);
                    var createP99ExceedsTenMs = snapshot.CreateTimeP99Seconds > 0.010;
                    var pressure = sustainedExceedsOneSecond || createP99ExceedsTenMs;

                    _output.WriteLine(
                        $"N={n} peakPending={snapshot.PeakPendingRequests} " +
                        $"peakUsage={snapshot.PeakUsage}/max={snapshot.MaxPoolSize} " +
                        $"createP99={snapshot.CreateTimeP99Seconds * 1000:F2}ms " +
                        $"longestSustainedPending={snapshot.LongestSustainedPending.TotalMilliseconds:F1}ms " +
                        $"createSamples={snapshot.CreateTimeSampleCount} " +
                        $"→ {(pressure ? "PRESSURE" : "no pressure")}");

                    verdicts.Add((n, snapshot, pressure));
                }

                var anyPressure = verdicts.Any(v => v.Pressure);
                _output.WriteLine("");
                _output.WriteLine(
                    "Note: Npgsql 10.0.3 ships db.client.connection.npgsql.create_time (connection-establishment cost) but no " +
                    "wait_time instrument the issue spec assumed. The verdict substitutes create_time p99 > 10ms as a pressure " +
                    "proxy (slow new-connection creation under load); the primary pressure signal remains pending_requests " +
                    "sustained > 1 s.");
                _output.WriteLine("");
                _output.WriteLine(anyPressure
                    ? "VERDICT: PRESSURE OBSERVED — the prior pooling evidence is stale; the NpgsqlDataSource singleton refactor is motivated."
                    : "VERDICT: NO PRESSURE — pooling is not the bottleneck; the plateau is something else (grain-turn serialisation, row contention, network floor).");
            }
            finally
            {
                await cluster.DisposeAsync();
            }
        }
        finally
        {
            ActiveProbeRuntime.Current = null;
        }
    }

    static async Task DriveIssuersAsync(
        NpgsqlPoolListener listener,
        IEdictSender sender,
        Guid[] aggregatePool,
        int parallelism,
        TimeSpan duration)
    {
        // Filler is reused per-issuer because Send serialises the command
        // synchronously and the buffer reference never escapes into async
        // use. Mirrors the production ClosedLoopRunner's FillerBuffer.
        var filler = new byte[256];

        using var cts = new CancellationTokenSource(duration);

        // Observable instruments (usage, max) only publish when something
        // pulls them. Without a sampler the listener would only see the
        // single end-of-window pull — masking intra-window peaks. 100 ms
        // gives ~150 samples over the 15 s window, dense enough that a
        // sustained-pressure interval can't slip between samples.
        using var sampleTimer = new Timer(_ =>
        {
            try
            {
                listener.RecordObservables();
            }
            catch
            {
                // The timer fires off-thread; if Capture/Reset is concurrently
                // running on another thread the listener's internal lock will
                // serialise. Any unexpected MeterListener exception during a
                // sample is non-fatal — we just lose that sample.
            }
        }, state: null, dueTime: TimeSpan.FromMilliseconds(100), period: TimeSpan.FromMilliseconds(100));

        var tasks = new Task[parallelism];
        for (var i = 0; i < parallelism; i++)
        {
            var issuerIndex = i;
            tasks[i] = Task.Run(async () =>
            {
                var index = issuerIndex;
                while (!cts.IsCancellationRequested)
                {
                    var aggregateId = aggregatePool[(int)(((uint)index) % aggregatePool.Length)];
                    try
                    {
                        await sender.Send(new BenchIncrementCommand(aggregateId, filler));
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // Failures are not what the probe is measuring — the
                        // listener picks up pool pressure regardless of which
                        // calls succeed. Swallow so the issuer keeps pressing.
                    }
                    index += parallelism;
                }
            });
        }
        await Task.WhenAll(tasks);
    }

    static class ActiveProbeRuntime
    {
        // Same cross-process bridge pattern Edict.Benchmarks.Throughput's
        // ClusterHarness uses — TestClusterBuilder's parameterless-ctor
        // configurators need a static to read the live runtime from. xunit
        // is serial in this assembly (see xunit.runner.json), so one bridge
        // at a time is safe.
        public static ISubstrateRuntime? Current { get; set; }

        public sealed class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                var runtime = Current ?? throw new InvalidOperationException(
                    "ActiveProbeRuntime.Current was null when the silo configurator ran.");
                runtime.ConfigureSilo(siloBuilder);

                // Mirror ClusterHarness's wrong-mode projection filter for
                // ClosedLoop. Without this the Counter projection activates on
                // every BenchEvent alongside BenchProjectionBuilder, doubling
                // consumer write pressure and biasing pool measurements that
                // are meant to mirror the published Commands-sweep baseline.
                siloBuilder.Services.PostConfigure<GrainTypeOptions>(o =>
                {
                    o.Classes.Remove(typeof(BenchCounterProjectionBuilder));
                });
            }
        }

        public sealed class ClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            {
                var runtime = Current ?? throw new InvalidOperationException(
                    "ActiveProbeRuntime.Current was null when the client configurator ran.");
                runtime.ConfigureClient(clientBuilder);
                // Commands scenario only needs IEdictSender — no workload row
                // repository registration required, unlike the Events sweep.
            }
        }
    }
}
