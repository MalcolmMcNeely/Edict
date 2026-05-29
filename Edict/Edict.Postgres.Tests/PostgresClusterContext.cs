using System.Collections.Concurrent;

using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

namespace Edict.Postgres.Tests;

/// <summary>
/// Per-fixture-instance bag of substrate-derived state the silo configurator
/// needs at <c>ISiloConfigurator.Configure</c> time. Lives in a registry
/// because <see cref="Orleans.TestingHost.ISiloConfigurator"/> implementations
/// are instantiated by Orleans (we cannot pass fixture state through their
/// constructor); the fixture writes its registry key into the
/// <see cref="Orleans.TestingHost.TestClusterBuilder.Properties"/> dictionary
/// and the configurator reads the same key off the silo's
/// <see cref="IConfiguration"/>.
/// </summary>
sealed record PostgresClusterContext(
    string PostgresConnectionString,
    string AzuriteConnectionString,
    TableServiceClient TableServiceClient,
    BlobServiceClient BlobServiceClient,
    QueueServiceClient QueueServiceClient,
    string DeadLetterTableName,
    string ClaimCheckTableName,
    string DatabaseName);

static class PostgresClusterContextRegistry
{
    public const string ContextKeyProperty = "Edict.Postgres.Tests.ClusterContextKey";

    static readonly ConcurrentDictionary<string, PostgresClusterContext> _entries = new();

    public static string Register(PostgresClusterContext context)
    {
        var key = Guid.NewGuid().ToString("N");
        _entries[key] = context;
        return key;
    }

    public static PostgresClusterContext Get(string key) =>
        _entries.TryGetValue(key, out var v)
            ? v
            : throw new InvalidOperationException(
                $"No cluster context registered for key '{key}'.");

    public static void Unregister(string key) => _entries.TryRemove(key, out _);
}
