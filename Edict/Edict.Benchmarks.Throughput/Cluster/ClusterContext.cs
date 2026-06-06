using System.Collections.Concurrent;

using Edict.Substrate;

namespace Edict.Benchmarks.Throughput.Cluster;

/// <summary>
/// The per-run bag the throughput cluster configurators need at
/// <c>ISiloConfigurator</c>/<c>IClientBuilderConfigurator</c> time: the substrate
/// <see cref="ISubstrateRuntime"/> the run brought up and the
/// <see cref="SubstrateStartMode"/> that selects which projection species is
/// active. Lives in a registry because Orleans constructs the configurators with
/// their parameterless ctors, so the harness writes a key into
/// <see cref="Orleans.TestingHost.TestClusterBuilder.Properties"/> and the
/// configurators read it back off configuration. The runtime's lifetime stays the
/// harness's (disposed via <c>await using</c>); this only holds a reference for the
/// run's duration.
/// </summary>
public sealed record ClusterContext(ISubstrateRuntime Runtime, SubstrateStartMode Mode);

/// <summary>
/// GUID-keyed registry that bridges a per-run <see cref="ClusterContext"/> into the
/// Orleans-constructed throughput configurators, mirroring the keyed-context idiom
/// the pairing and persistence fixtures use. Each run registers its context, passes
/// the key through <see cref="Orleans.TestingHost.TestClusterBuilder.Properties"/>,
/// and unregisters on teardown.
/// </summary>
public static class ClusterContextRegistry
{
    public const string ContextKeyProperty = "Edict.Benchmarks.Throughput.Cluster.ClusterContextKey";

    static readonly ConcurrentDictionary<string, ClusterContext> _entries = new();

    public static string Register(ClusterContext context)
    {
        var key = Guid.NewGuid().ToString("N");
        _entries[key] = context;
        return key;
    }

    public static ClusterContext Get(string key) =>
        _entries.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"No throughput cluster context registered for key '{key}'.");

    public static void Unregister(string key) => _entries.TryRemove(key, out _);
}
