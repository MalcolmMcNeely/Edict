using Edict.Benchmarks.Throughput.Workload;
using Edict.Contracts.TableStorage;
using Edict.Substrate;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Orleans.Hosting;
using Orleans.TestingHost;

namespace Edict.Benchmarks.Throughput.Cluster;

/// <summary>
/// Brings up a substrate runtime + Orleans <see cref="TestCluster"/> for one
/// measurement pass and tears them down on exit. Both methodologies — the
/// closed-loop sweep and the saturation pass — go through here so the
/// substrate-bridge static and the Silo/Client configurators live in one
/// place. The workload-specific repository registrations land on the client
/// builder here too: the substrate seam (ADR-0030) stays workload-free, so
/// the harness is the layer that knows about <c>BenchEventRow</c> /
/// <c>BenchCounterRow</c>.
/// </summary>
public static class ClusterHarness
{
    public static async Task<TResult> RunAsync<TResult>(
        ISubstrate substrate,
        SubstrateStartMode mode,
        Func<TestCluster, Task<TResult>> body,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(substrate);
        ArgumentNullException.ThrowIfNull(body);

        await using var runtime = await substrate.StartAsync(ct, mode);

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
                return await body(cluster);
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

    static class ActiveRuntime
    {
        // Cross-process bridge for TestClusterBuilder's parameterless-ctor
        // configurators. The harness serialises substrate runs so a static is
        // safe — one cluster up at a time.
        public static ISubstrateRuntime? Current { get; set; }

        public sealed class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                var runtime = Current ?? throw new InvalidOperationException(
                    "ClusterHarness.ActiveRuntime was null when the silo configurator ran.");
                runtime.ConfigureSilo(siloBuilder);
            }
        }

        public sealed class ClientConfigurator : IClientBuilderConfigurator
        {
            public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
            {
                var runtime = Current ?? throw new InvalidOperationException(
                    "ClusterHarness.ActiveRuntime was null when the client configurator ran.");
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
