using System.Collections.Concurrent;

using Edict.Contracts.Configuration;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Postgres;
using Edict.Tests.Conformance;
using Edict.Tests.Conformance.Outbox;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orleans.Configuration;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Postgres.Tests;

/// <summary>
/// Deploys a silo wired through <see cref="EdictPostgresSiloBuilderExtensions.AddEdictPostgresPersistence"/>
/// over a per-fixture database in the shared testcontainer, so the schema bootstrap
/// the provider runs at silo wire-up is present, and exposes the database connection
/// string for the provider-internal tests to open their own data sources against. The
/// reference stream is a dumb <c>MemoryStreams</c> — this fixture exists to stand the
/// Postgres persistence provider up, not to exercise a streaming path, so it pulls in
/// no streaming backend.
/// </summary>
public sealed class PostgresProviderFixture : IAsyncLifetime
{
    string _connectionString = "";
    string _contextKey = "";

    public string PostgresConnectionString => _connectionString;

    public string DeadLetterTableName => "edict_dead_letter";

    public string ClaimCheckTableName => "edict_claim_check";

    public async Task InitializeAsync()
    {
        var adminConnectionString = await PostgresAssemblyHost.GetAdminConnectionStringAsync();
        var databaseName = $"edict_{Guid.NewGuid():N}";
        _connectionString = await PostgresDatabaseFactory.CreateDatabaseAsync(adminConnectionString, databaseName);

        var context = new ProviderContext(_connectionString, DeadLetterTableName, ClaimCheckTableName);
        _contextKey = ProviderContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Properties[ProviderContextRegistry.ContextKeyProperty] = _contextKey;
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        _cluster = cluster;
    }

    TestCluster _cluster = null!;

    public async Task DisposeAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.DisposeAsync();
        }
        ProviderContextRegistry.Unregister(_contextKey);
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(CounterAggregate).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(Orleans.Hosting.ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[ProviderContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing from silo configuration.");
            var ctx = ProviderContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            // A cold Postgres testcontainer's first DDL bootstrap can run past
            // Orleans' default 30 s response timeout on a slow runner.
            siloBuilder.Configure<SiloMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromMinutes(2));
            siloBuilder.Services.AddSingleton(TimeProvider.System);
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
            siloBuilder.AddEdict();
            siloBuilder.AddMemoryStreams("edict");
            siloBuilder.AddEdictPostgresPersistence(persistenceOptions =>
            {
                persistenceOptions.ConnectionString = ctx.ConnectionString;
                persistenceOptions.DeadLetterTableName = ctx.DeadLetterTableName;
                persistenceOptions.ClaimCheckTableName = ctx.ClaimCheckTableName;
            });
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(IConfiguration configuration, IClientBuilder clientBuilder)
        {
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Configure<ClientMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromMinutes(2));
            clientBuilder.Services.AddEdict();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresProviderCollection : ICollectionFixture<PostgresProviderFixture>
{
    public const string Name = "PostgresProvider";
}

sealed record ProviderContext(string ConnectionString, string DeadLetterTableName, string ClaimCheckTableName);

static class ProviderContextRegistry
{
    public const string ContextKeyProperty = "Edict.Postgres.Tests.ProviderContextKey";

    static readonly ConcurrentDictionary<string, ProviderContext> _entries = new();

    public static string Register(ProviderContext context)
    {
        var key = Guid.NewGuid().ToString("N");
        _entries[key] = context;
        return key;
    }

    public static ProviderContext Get(string key) =>
        _entries.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"No provider context registered for key '{key}'.");

    public static void Unregister(string key) => _entries.TryRemove(key, out _);
}
