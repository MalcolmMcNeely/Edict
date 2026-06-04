using System.Collections.Concurrent;

using Edict.Tests.Conformance.Outbox;
using Edict.Tests.Conformance.Streaming.References;

namespace Edict.Azure.Streaming.Tests;

/// <summary>
/// Per-fixture-instance bag the streaming silo configurator needs at
/// <c>ISiloConfigurator.Configure</c> time. The streaming axis stands up a real
/// AQS stream over the assembly-shared Azurite but keeps persistence in-process:
/// the reference claim-check store and table store factory are <em>shared
/// instances</em> so every silo in the cluster reads and writes the same
/// in-memory state (a publisher on one silo, a receiver on another). Lives in a
/// registry because <see cref="Orleans.TestingHost.ISiloConfigurator"/> instances
/// are constructed by Orleans — the fixture writes its key into
/// <see cref="Orleans.TestingHost.TestClusterBuilder.Properties"/> and the
/// configurator reads it back off the silo's configuration.
/// </summary>
sealed record AqsStreamingClusterContext(
    string ConnectionString,
    ReferenceClaimCheckStore ClaimCheckStore,
    ReferenceTableStoreFactory TableStoreFactory,
    StorageFaultState StorageFault);

static class AqsStreamingClusterContextRegistry
{
    public const string ContextKeyProperty = "Edict.Azure.Streaming.Tests.ClusterContextKey";

    static readonly ConcurrentDictionary<string, AqsStreamingClusterContext> _entries = new();

    public static string Register(AqsStreamingClusterContext context)
    {
        var key = Guid.NewGuid().ToString("N");
        _entries[key] = context;
        return key;
    }

    public static AqsStreamingClusterContext Get(string key) =>
        _entries.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"No streaming cluster context registered for key '{key}'.");

    public static void Unregister(string key) => _entries.TryRemove(key, out _);
}
