using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.TableStorage;
using Edict.Substrate;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace Edict.Benchmarks.Throughput.Cluster;

/// <summary>
/// Brings up a substrate runtime + Orleans <see cref="TestCluster"/> for one
/// measurement pass and tears them down on exit. Both methodologies — the
/// closed-loop sweep and the saturation pass — go through here so the
/// substrate-bridge registry and the Silo/Client configurators live in one
/// place. The workload-specific repository registrations land on the client
/// builder here too: the substrate seam stays workload-free, so
/// the harness is the layer that knows about <c>BenchEventRow</c> /
/// <c>BenchCounterRow</c>.
/// <para>
/// Per-<see cref="SubstrateStartMode"/> projection registration is the
/// load-bearing decision: Orleans assembly-scans the workload assembly and
/// would otherwise activate all three of <see cref="BenchProjectionBuilder"/>,
/// <see cref="BenchCounterListProjectionBuilder"/>, and
/// <see cref="BenchCounterStateProjectionBuilder"/> in every cluster, multiplying
/// the consumer write pressure each event drives. The silo configurator removes
/// the two non-active projections from <see cref="GrainTypeOptions"/> before the
/// manifest materialises; the client configurator registers only the matching read
/// surface (a store for the closed-loop and List passes, nothing for the State
/// pass — it reads through the projection reader).
/// </para>
/// </summary>
public static class ClusterHarness
{
    public static async Task<TResult> RunAsync<TResult>(
        ISubstrate substrate,
        SubstrateStartMode mode,
        Func<TestCluster, Task<TResult>> body,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(substrate);
        ArgumentNullException.ThrowIfNull(body);

        await using var runtime = await substrate.StartAsync(cancellationToken, mode);

        var contextKey = ClusterContextRegistry.Register(new ClusterContext(runtime, mode));
        try
        {
            var clusterBuilder = new TestClusterBuilder();
            // Single silo per run is the documented setup
            // (docs/benchmarks/throughput.md § Setup: "Single Orleans
            // TestCluster silo per substrate run") and matches the assumption
            // EdictPostgresPersistenceOptions.MaxPoolSize bakes in
            // ("Default 200 gives a single silo 2× headroom against the
            // published N = 256 closed-loop sweep point"). TestClusterBuilder
            // defaults to 2 silos; leaving the default would double the silo-
            // side Npgsql connection demand and overrun what the framework
            // defaults plan for, surfacing as bursts of "Failed to connect to
            // 127.0.0.1:<port>" during the saturation pass.
            clusterBuilder.Options.InitialSilosCount = 1;
            clusterBuilder.Properties[ClusterContextRegistry.ContextKeyProperty] = contextKey;
            clusterBuilder.AddSiloBuilderConfigurator<SiloConfigurator>();
            clusterBuilder.AddClientBuilderConfigurator<ClientConfigurator>();
            var cluster = clusterBuilder.Build();
            await cluster.DeployAsync();
            try
            {
                return await body(cluster);
            }
            finally
            {
                await cluster.DisposeAsync();
            }
        }
        finally
        {
            ClusterContextRegistry.Unregister(contextKey);
        }
    }

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[ClusterContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing from silo configuration.");
            var context = ClusterContextRegistry.Get(key);
            context.Runtime.ConfigureSilo(siloBuilder);

            // All three projection grains subscribe to the Bench stream by way
            // of [ImplicitStreamSubscription]. Each mode measures exactly one;
            // removing the other two from GrainTypeOptions.Classes drops them
            // from the manifest before the ImplicitStreamSubscriberTable
            // consults it, so the stream provider never activates them on a
            // Bench event and one pass never contaminates another's consumer
            // write pressure.
            var inactiveProjections = context.Mode switch
            {
                SubstrateStartMode.ClosedLoop =>
                    new[] { typeof(BenchCounterListProjectionBuilder), typeof(BenchCounterStateProjectionBuilder) },
                SubstrateStartMode.SaturationList =>
                    new[] { typeof(BenchProjectionBuilder), typeof(BenchCounterStateProjectionBuilder) },
                SubstrateStartMode.SaturationState =>
                    new[] { typeof(BenchProjectionBuilder), typeof(BenchCounterListProjectionBuilder) },
                _ => throw new ArgumentOutOfRangeException(nameof(siloBuilder), context.Mode, "Unhandled substrate start mode."),
            };
            siloBuilder.Services.PostConfigure<GrainTypeOptions>(grainTypeOptions =>
            {
                foreach (var inactiveProjection in inactiveProjections)
                {
                    grainTypeOptions.Classes.Remove(inactiveProjection);
                }
            });
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            var key = configuration[ClusterContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing from client configuration.");
            var context = ClusterContextRegistry.Get(key);
            context.Runtime.ConfigureClient(clientBuilder);
            // Workload-specific repository — the substrate library stays
            // workload-free, so the harness registers its own row type
            // here. The runtime's CreateRowStore<T> seam picks the
            // substrate-correct store (AzureTableWriteStore on Azurite,
            // PostgresTableWriteStore on Kafka+Postgres) without the
            // harness branching on substrate kind. Reads go store-direct
            // (off-grain — the right shape for a latency measurement). Only
            // the mode's read surface registers — the closed-loop sweep reads
            // BenchEventRow store-direct, the List saturation pass reads the
            // BenchCounterRow store, and the State saturation pass reads no
            // table at all: it sums through the AddEdict()-registered
            // IEdictProjectionReader<BenchCounterProjection> (a grain hop), so
            // no store is wired here.
            switch (context.Mode)
            {
                case SubstrateStartMode.ClosedLoop:
                    clientBuilder.Services.AddSingleton<IEdictTableWriteStore<BenchEventRow>>(serviceProvider =>
                        context.Runtime.CreateRowStore<BenchEventRow>(serviceProvider, BenchProjectionBuilder.TableNameLiteral));
                    break;
                case SubstrateStartMode.SaturationList:
                    clientBuilder.Services.AddSingleton<IEdictTableWriteStore<BenchCounterRow>>(serviceProvider =>
                        context.Runtime.CreateRowStore<BenchCounterRow>(serviceProvider, BenchCounterListProjectionBuilder.TableNameLiteral));
                    break;
                case SubstrateStartMode.SaturationState:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(configuration), context.Mode, "Unhandled substrate start mode.");
            }
        }
    }
}
