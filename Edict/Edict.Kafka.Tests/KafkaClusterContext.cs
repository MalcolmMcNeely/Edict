using System.Collections.Concurrent;

namespace Edict.Kafka.Tests;

/// <summary>
/// Per-fixture-instance bag of substrate-derived state the silo configurator
/// needs at <c>ISiloConfigurator.Configure</c> time. Lives in a registry
/// because Orleans instantiates configurators itself; the fixture writes its
/// registry key into <c>TestClusterBuilder.Properties</c> and the configurator
/// reads it off the silo's <c>IConfiguration</c>. Same pattern as
/// <c>PostgresClusterContext</c>.
/// </summary>
sealed record KafkaClusterContext(
    string KafkaBootstrapServers,
    string KafkaConsumerGroup,
    string PostgresConnectionString,
    string DeadLetterTableName,
    string ClaimCheckTableName,
    string DatabaseName);

static class KafkaClusterContextRegistry
{
    public const string ContextKeyProperty = "Edict.Kafka.Tests.ClusterContextKey";

    static readonly ConcurrentDictionary<string, KafkaClusterContext> _entries = new();

    public static string Register(KafkaClusterContext context)
    {
        var key = Guid.NewGuid().ToString("N");
        _entries[key] = context;
        return key;
    }

    public static KafkaClusterContext Get(string key) =>
        _entries.TryGetValue(key, out var v)
            ? v
            : throw new InvalidOperationException(
                $"No cluster context registered for key '{key}'.");

    public static void Unregister(string key) => _entries.TryRemove(key, out _);
}
