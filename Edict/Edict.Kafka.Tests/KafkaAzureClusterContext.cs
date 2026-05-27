using System.Collections.Concurrent;

using Azure.Data.Tables;
using Azure.Storage.Blobs;

using Edict.Contracts.ClaimCheck;

namespace Edict.Kafka.Tests;

/// <summary>
/// Per-fixture-instance bag of substrate state the silo configurator needs at
/// <c>ISiloConfigurator.Configure</c> time. Pairs Kafka-broker coordinates
/// with Azurite-namespaced container/table names so two collections cannot
/// race on each other's <c>edict-state</c> blob container or
/// <c>edict_dead_letter</c> table inside the shared Azurite. Same registry
/// indirection as <see cref="KafkaClusterContext"/> — Orleans instantiates
/// the configurator itself, so the fixture writes its registry key into
/// <see cref="Orleans.TestingHost.TestClusterBuilder.Properties"/> and the
/// configurator reads it back off the silo's
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.
/// </summary>
sealed record KafkaAzureClusterContext(
    string KafkaBootstrapServers,
    string KafkaConsumerGroup,
    string AzureConnectionString,
    TableServiceClient TableServiceClient,
    BlobServiceClient BlobServiceClient,
    string GrainStateContainerName,
    string DeadLetterTableName,
    string ClaimCheckContainerName,
    IEdictClaimCheckStore ClaimCheckStore);

static class KafkaAzureClusterContextRegistry
{
    public const string ContextKeyProperty = "Edict.Kafka.Tests.AzureClusterContextKey";

    static readonly ConcurrentDictionary<string, KafkaAzureClusterContext> _entries = new();

    public static string Register(KafkaAzureClusterContext context)
    {
        var key = Guid.NewGuid().ToString("N");
        _entries[key] = context;
        return key;
    }

    public static KafkaAzureClusterContext Get(string key) =>
        _entries.TryGetValue(key, out var v)
            ? v
            : throw new InvalidOperationException(
                $"No cluster context registered for key '{key}'.");

    public static void Unregister(string key) => _entries.TryRemove(key, out _);
}
