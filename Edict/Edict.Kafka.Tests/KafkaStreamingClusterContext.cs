using System.Collections.Concurrent;

using Edict.Tests.Conformance.Streaming.References;

namespace Edict.Kafka.Tests;

/// <summary>
/// Per-fixture-instance bag the Kafka streaming silo configurator needs at
/// <c>ISiloConfigurator.Configure</c> time. The streaming axis stands up a real
/// Kafka stream over the assembly-shared broker (each fixture mints its own
/// consumer group so offsets don't collide) but keeps persistence in-process:
/// the reference claim-check store and table store factory are <em>shared
/// instances</em> so every silo in the cluster reads and writes the same
/// in-memory state. Lives in a registry because
/// <see cref="Orleans.TestingHost.ISiloConfigurator"/> instances are constructed
/// by Orleans — the fixture writes its key into
/// <see cref="Orleans.TestingHost.TestClusterBuilder.Properties"/> and the
/// configurator reads it back off the silo's configuration.
/// </summary>
sealed record KafkaStreamingClusterContext(
    string BootstrapServers,
    string ConsumerGroup,
    ReferenceClaimCheckStore ClaimCheckStore,
    ReferenceTableStoreFactory TableStoreFactory);

static class KafkaStreamingClusterContextRegistry
{
    public const string ContextKeyProperty = "Edict.Kafka.Tests.StreamingClusterContextKey";

    static readonly ConcurrentDictionary<string, KafkaStreamingClusterContext> _entries = new();

    public static string Register(KafkaStreamingClusterContext context)
    {
        var key = Guid.NewGuid().ToString("N");
        _entries[key] = context;
        return key;
    }

    public static KafkaStreamingClusterContext Get(string key) =>
        _entries.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"No Kafka streaming cluster context registered for key '{key}'.");

    public static void Unregister(string key) => _entries.TryRemove(key, out _);
}
