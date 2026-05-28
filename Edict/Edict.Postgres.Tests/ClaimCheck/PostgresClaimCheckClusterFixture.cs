using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure;
using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.ClaimCheck;
using Edict.Core.Commands;
using Edict.Core.Outbox;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Postgres;
using Edict.Postgres.ClaimCheck;
using Edict.Postgres.TableStorage;
using Edict.Tests.Conformance;
using Edict.Tests.Conformance.ClaimCheck;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Orleans;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Postgres.Tests.ClaimCheck;

public sealed class PostgresClaimCheckClusterFixture : ClaimCheckFixture
{
    string _databaseConnectionString = "";
    string _azuriteConnectionString = "";
    TableServiceClient _tableServiceClient = null!;
    BlobServiceClient _blobServiceClient = null!;
    QueueServiceClient _queueServiceClient = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public override IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public override IGrainFactory GrainFactory => Cluster.GrainFactory;

    public override IEdictTableRepository<T> GetTableRepository<T>(string tableName) =>
        new PostgresTableRepository<T>(
            _databaseConnectionString,
            tableName,
            Cluster.Client.ServiceProvider.GetRequiredService<Serializer>());

    public override IEdictTableStoreFactory TableStoreFactory =>
        new PostgresTableWriteStoreFactory(
            _databaseConnectionString,
            Cluster.Client.ServiceProvider.GetRequiredService<Serializer>());

    public override async Task<bool> ClaimCheckBlobExistsAsync(string key)
    {
        // The Postgres claim-check store generates GUID-N keys and stores the
        // payload in a single bytea column. "Blob existence" maps to "row exists
        // in edict_claim_check for this id".
        if (!Guid.TryParseExact(key, "N", out var id))
        {
            return false;
        }
        await using var connection = new NpgsqlConnection(_databaseConnectionString);
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM edict_claim_check WHERE id = @id;";
        cmd.Parameters.AddWithValue("id", id);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null && result is not DBNull;
    }

    public string DeadLetterTableName { get; private set; } = "edict_dead_letter";

    public string ClaimCheckTableName { get; private set; } = "edict_claim_check";

    public override async Task InitializeAsync()
    {
        var adminConnectionString = await PostgresAssemblyHost.GetAdminConnectionStringAsync();
        _azuriteConnectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();

        var databaseName = $"edict_{Guid.NewGuid():N}";
        _databaseConnectionString = await PostgresDatabaseFactory.CreateDatabaseAsync(adminConnectionString, databaseName);

        _tableServiceClient = new TableServiceClient(_azuriteConnectionString);
        _blobServiceClient = new BlobServiceClient(_azuriteConnectionString);
        _queueServiceClient = new QueueServiceClient(_azuriteConnectionString);

        var context = new PostgresClusterContext(
            _databaseConnectionString,
            _azuriteConnectionString,
            _tableServiceClient,
            _blobServiceClient,
            _queueServiceClient,
            DeadLetterTableName,
            ClaimCheckTableName,
            databaseName);
        _contextKey = PostgresClusterContextRegistry.Register(context);

        var builder = new TestClusterBuilder();
        builder.Properties[PostgresClusterContextRegistry.ContextKeyProperty] = _contextKey;
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        builder.AddClientBuilderConfigurator<ClientConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public override async Task DisposeAsync()
    {
        if (Cluster is not null)
        {
            await Cluster.DisposeAsync();
        }
        PostgresClusterContextRegistry.Unregister(_contextKey);
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(ClaimCheckCounterAggregate).Assembly)
            .AddAssembly(typeof(IEdictCommandHandler).Assembly)
            .AddEdictContractSerializer();

    sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(Orleans.Hosting.ISiloBuilder siloBuilder)
        {
            var key = siloBuilder.Configuration[PostgresClusterContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing.");
            var ctx = PostgresClusterContextRegistry.Get(key);

            siloBuilder.AddActivityPropagation();
            siloBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            siloBuilder.Services.AddSingleton(ctx.TableServiceClient);
            siloBuilder.Services.AddSingleton(ctx.BlobServiceClient);
            siloBuilder.Services.AddSingleton(ctx.QueueServiceClient);
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
            siloBuilder.AddEdict();
            // 1-byte threshold forces every raised event onto the pointer
            // branch, exercising publish-via-Postgres + receiver-unwrap.
            siloBuilder.AddEdictAzureStreams(o =>
            {
                o.QueueServiceClient = ctx.QueueServiceClient;
                o.ClaimCheckThresholdBytes = 1;
                o.QueuePollingPeriod = TimeSpan.FromMilliseconds(200);
            });
            siloBuilder.AddEdictPostgresPersistence(o =>
            {
                o.ConnectionString = ctx.PostgresConnectionString;
                o.DeadLetterTableName = ctx.DeadLetterTableName;
                o.ClaimCheckTableName = ctx.ClaimCheckTableName;
            });
            // Re-register ClaimCheckPolicy with the actual Postgres store so
            // the 1-byte threshold has a store to write to (AddEdictAzureStreams
            // resolves it lazily and the registration is TryAddSingleton, so
            // doing this AFTER persistence binds the Postgres store).
            siloBuilder.Services.AddSingleton(sp => new ClaimCheckPolicy(
                sp.GetRequiredService<Serializer>(),
                thresholdBytes: 1,
                store: sp.GetRequiredService<IEdictClaimCheckStore>(),
                accessors: sp.GetRequiredService<IEventStreamAccessors>()));
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            IClientBuilder clientBuilder)
        {
            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresClaimCheckCollection : ICollectionFixture<PostgresClaimCheckClusterFixture>
{
    public const string Name = "PostgresClaimCheck";
}
