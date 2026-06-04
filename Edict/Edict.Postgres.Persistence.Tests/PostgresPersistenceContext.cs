using System.Collections.Concurrent;

using Edict.Contracts.Configuration;

namespace Edict.Postgres.Persistence.Tests;

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
/// subclass picks the knobs, the configurator applies them.
/// </summary>
sealed record PostgresPersistenceContext(
    string ConnectionString,
    string DeadLetterTableName,
    string ClaimCheckTableName,
    Action<EdictOptions>? ConfigureOptions,
    bool ReplacePublishExecutorWithControllable,
    bool DecorateGrainStorage,
    int? ClaimCheckThresholdBytes);

static class PostgresPersistenceContextRegistry
{
    public const string ContextKeyProperty = "Edict.Postgres.Persistence.Tests.ClusterContextKey";

    static readonly ConcurrentDictionary<string, PostgresPersistenceContext> _entries = new();

    public static string Register(PostgresPersistenceContext context)
    {
        var key = Guid.NewGuid().ToString("N");
        _entries[key] = context;
        return key;
    }

    public static PostgresPersistenceContext Get(string key) =>
        _entries.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"No persistence cluster context registered for key '{key}'.");

    public static void Unregister(string key) => _entries.TryRemove(key, out _);
}
