using System.Collections.Concurrent;

using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Contracts.ClaimCheck;

namespace Edict.Azure.Tests;

/// <summary>
/// Per-fixture-instance bag of Azurite-derived state the silo configurator
/// needs at <c>ISiloConfigurator.Configure</c> time. Lives in a registry
/// because <see cref="Orleans.TestingHost.ISiloConfigurator"/> implementations
/// are instantiated by Orleans (we cannot pass fixture state through their
/// constructor); the fixture writes its registry key into the
/// <see cref="Orleans.TestingHost.TestClusterBuilder.Properties"/> dictionary
/// and the configurator reads the same key off the silo's
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/>.
/// Keeping the (necessarily static) registry on a helper class — not on the
/// fixture itself — satisfies the "no <c>static</c> field in any
/// cluster fixture" rule that the previous shape violated.
/// </summary>
sealed record AzureClusterContext(
    string ConnectionString,
    TableServiceClient TableServiceClient,
    BlobServiceClient BlobServiceClient,
    QueueServiceClient QueueServiceClient,
    string GrainStateContainerName,
    string DeadLetterTableName,
    string ClaimCheckContainerName = "",
    IEdictClaimCheckStore? ClaimCheckStore = null);

static class AzureClusterContextRegistry
{
    public const string ContextKeyProperty = "Edict.Azure.Tests.ClusterContextKey";

    static readonly ConcurrentDictionary<string, AzureClusterContext> _entries = new();

    public static string Register(AzureClusterContext context)
    {
        var key = Guid.NewGuid().ToString("N");
        _entries[key] = context;
        return key;
    }

    public static AzureClusterContext Get(string key) =>
        _entries.TryGetValue(key, out var v)
            ? v
            : throw new InvalidOperationException(
                $"No cluster context registered for key '{key}'.");

    /// <summary>
    /// Replace an existing entry's context — needed when Azurite is restarted
    /// mid-fixture and its connection string + service clients change. The
    /// silo configurator captures whichever context the registry holds when a
    /// new grain activates, so refreshing the entry rather than minting a new
    /// key keeps the cluster builder's stamped property still valid.
    /// </summary>
    public static void Replace(string key, AzureClusterContext context) =>
        _entries[key] = context;

    public static void Unregister(string key) => _entries.TryRemove(key, out _);
}
