using Edict.Benchmarks.Throughput.Cluster;
using Edict.Contracts.TableStorage;
using Edict.Substrate;

using Orleans.Hosting;

namespace Edict.Benchmarks.Throughput.Tests;

/// <summary>
/// Pins the per-run context registry that bridges a substrate runtime + start mode
/// into the parameterless-ctor <c>TestCluster</c> configurators, mirroring how the
/// pairing and persistence fixtures carry their per-fixture context across the
/// Orleans-constructed configurator boundary. The harness owns the runtime's
/// lifetime; the registry holds only a reference for the run's duration, keyed by
/// GUID so each run resolves its own context and unregistering one leaves its peers
/// untouched.
/// </summary>
public sealed class ClusterContextRegistryTests
{
    [Fact]
    public void Get_ShouldReturnTheRegisteredContext_ForItsKey()
    {
        var context = new ClusterContext(new StubSubstrateRuntime(), SubstrateStartMode.ClosedLoop);

        var key = ClusterContextRegistry.Register(context);

        Assert.Same(context, ClusterContextRegistry.Get(key));
    }

    [Fact]
    public void Register_ShouldKeyEachContextSeparately()
    {
        var first = new ClusterContext(new StubSubstrateRuntime(), SubstrateStartMode.SaturationList);
        var second = new ClusterContext(new StubSubstrateRuntime(), SubstrateStartMode.SaturationState);

        var firstKey = ClusterContextRegistry.Register(first);
        var secondKey = ClusterContextRegistry.Register(second);

        Assert.NotEqual(firstKey, secondKey);
        Assert.Same(first, ClusterContextRegistry.Get(firstKey));
        Assert.Same(second, ClusterContextRegistry.Get(secondKey));
    }

    [Fact]
    public void Get_ShouldThrow_AfterUnregister()
    {
        var key = ClusterContextRegistry.Register(
            new ClusterContext(new StubSubstrateRuntime(), SubstrateStartMode.ClosedLoop));

        ClusterContextRegistry.Unregister(key);

        Assert.Throws<InvalidOperationException>(() => ClusterContextRegistry.Get(key));
    }

    [Fact]
    public void Unregister_ShouldLeavePeerKeysResolvable()
    {
        var survivor = new ClusterContext(new StubSubstrateRuntime(), SubstrateStartMode.ClosedLoop);
        var survivorKey = ClusterContextRegistry.Register(survivor);
        var evictedKey = ClusterContextRegistry.Register(
            new ClusterContext(new StubSubstrateRuntime(), SubstrateStartMode.SaturationList));

        ClusterContextRegistry.Unregister(evictedKey);

        Assert.Same(survivor, ClusterContextRegistry.Get(survivorKey));
        Assert.Throws<InvalidOperationException>(() => ClusterContextRegistry.Get(evictedKey));
    }

    sealed class StubSubstrateRuntime : ISubstrateRuntime
    {
        public Action<ISiloBuilder> ConfigureSilo => _ => { };

        public Action<IClientBuilder> ConfigureClient => _ => { };

        public IEdictTableWriteStore<TRow> CreateRowStore<TRow>(IServiceProvider serviceProvider, string tableName)
            where TRow : class, new() =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
