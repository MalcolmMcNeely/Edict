using System.Collections.Concurrent;

using Azure.Data.Tables;
using Azure.Storage.Blobs;

using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Tests.Conformance.Outbox;

namespace Edict.Azure.Persistence.Tests;

/// <summary>
/// Per-fixture-instance bag the persistence silo configurator needs at
/// <c>ISiloConfigurator.Configure</c> time. Lives in a registry because
/// <see cref="Orleans.TestingHost.ISiloConfigurator"/> instances are constructed
/// by Orleans — the fixture writes its key into
/// <see cref="Orleans.TestingHost.TestClusterBuilder.Properties"/> and the
/// configurator reads it back off the silo's configuration. The knob fields
/// (<see cref="ConfigureOptions"/>, <see cref="ReplacePublishExecutorWithControllable"/>,
/// <see cref="DecorateGrainStorage"/>, <see cref="ClaimCheckThresholdBytes"/>)
/// let one configurator stand up every persistence-axis fixture shape; a
/// subclass picks the knobs, the configurator applies them. The fixture-owned
/// <see cref="OutboxFault"/> / <see cref="StorageFault"/> instances are carried
/// here so the configurator can wire the controllable executor and grain-storage
/// decorator to the exact switches the fixture's scenarios flip.
/// </summary>
sealed record AzurePersistenceContext(
    TableServiceClient TableServiceClient,
    BlobServiceClient BlobServiceClient,
    string GrainStateContainerName,
    string DeadLetterTableName,
    IEdictClaimCheckStore ClaimCheckStore,
    Action<EdictOptions>? ConfigureOptions,
    bool ReplacePublishExecutorWithControllable,
    bool DecorateGrainStorage,
    int? ClaimCheckThresholdBytes,
    OutboxFaultState OutboxFault,
    StorageFaultState StorageFault,
    TimeProvider? ClockOverride);

static class AzurePersistenceContextRegistry
{
    public const string ContextKeyProperty = "Edict.Azure.Persistence.Tests.ClusterContextKey";

    static readonly ConcurrentDictionary<string, AzurePersistenceContext> _entries = new();

    public static string Register(AzurePersistenceContext context)
    {
        var key = Guid.NewGuid().ToString("N");
        _entries[key] = context;
        return key;
    }

    public static AzurePersistenceContext Get(string key) =>
        _entries.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"No persistence cluster context registered for key '{key}'.");

    public static void Unregister(string key) => _entries.TryRemove(key, out _);
}
