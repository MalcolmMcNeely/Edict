using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure.Streaming;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.Sending;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Commands;
using Edict.Core.Serialization;
using Edict.Core.TableStorage;
using Edict.Postgres;
using Edict.Postgres.TableStorage;
using Edict.Tests.Conformance;
using Edict.Tests.Conformance.Idempotency;

using FluentValidation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

using Npgsql;

using Orleans;
using Orleans.Serialization;
using Orleans.TestingHost;

namespace Edict.Postgres.Tests.Idempotency;

public sealed class IdempotencyWindowSizePostgresFixture : IdempotencyWindowSizeFixture
{
    public const int ConfiguredWindowSizeValue = 2;

    string _adminConnectionString = "";
    string _databaseConnectionString = "";
    string _azuriteConnectionString = "";
    NpgsqlDataSource _dataSource = null!;
    TableServiceClient _tableServiceClient = null!;
    BlobServiceClient _blobServiceClient = null!;
    QueueServiceClient _queueServiceClient = null!;
    string _contextKey = "";

    public TestCluster Cluster { get; private set; } = null!;

    public override int ConfiguredWindowSize => ConfiguredWindowSizeValue;

    public override IEdictSender Sender =>
        Cluster.Client.ServiceProvider.GetRequiredService<IEdictSender>();

    public override IGrainFactory GrainFactory => Cluster.GrainFactory;

    public override IEdictTableRepository<T> GetTableRepository<T>(string tableName) =>
        new PostgresTableRepository<T>(
            _dataSource,
            tableName,
            Cluster.Client.ServiceProvider.GetRequiredService<Serializer>());

    public override IEdictTableStoreFactory TableStoreFactory =>
        new PostgresTableWriteStoreFactory(
            _dataSource,
            Cluster.Client.ServiceProvider.GetRequiredService<Serializer>());

    public string DeadLetterTableName { get; private set; } = "edict_dead_letter";

    public override async Task InitializeAsync()
    {
        _adminConnectionString = await PostgresAssemblyHost.GetAdminConnectionStringAsync();
        _azuriteConnectionString = await AzuriteAssemblyHost.GetConnectionStringAsync();

        var databaseName = $"edict_{Guid.NewGuid():N}";
        _databaseConnectionString = await PostgresDatabaseFactory.CreateDatabaseAsync(_adminConnectionString, databaseName);
        _dataSource = new NpgsqlDataSourceBuilder(_databaseConnectionString).Build();

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
            "edict_claim_check",
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
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }
        PostgresClusterContextRegistry.Unregister(_contextKey);
    }

    static void ConfigureEdictSerialization(ISerializerBuilder serializer) =>
        serializer
            .AddAssembly(typeof(OrderCommandHandler).Assembly)
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
            siloBuilder.Services.AddSingleton<IValidator<ValidateSkuCommand>, SkuRequiredValidator>();
            siloBuilder.Services.AddSingleton<IValidator<StateCheckCommand>, GrainStateRequiredValidator>();
            siloBuilder.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();
            siloBuilder.AddEdict(o => o.IdempotencyWindowSize = ConfiguredWindowSizeValue);
            siloBuilder.AddEdictAzureStreams(o =>
            {
                o.QueueServiceClient = ctx.QueueServiceClient;
                o.QueuePollingPeriod = TimeSpan.FromMilliseconds(200);
            });
            siloBuilder.AddEdictPostgresPersistence(o =>
            {
                o.ConnectionString = ctx.PostgresConnectionString;
                o.DeadLetterTableName = ctx.DeadLetterTableName;
                o.ClaimCheckTableName = ctx.ClaimCheckTableName;
            });
        }
    }

    sealed class ClientConfigurator : IClientBuilderConfigurator
    {
        public void Configure(
            IConfiguration configuration,
            IClientBuilder clientBuilder)
        {
            var key = configuration[PostgresClusterContextRegistry.ContextKeyProperty]
                ?? throw new InvalidOperationException("ClusterContextKey missing.");
            var ctx = PostgresClusterContextRegistry.Get(key);

            clientBuilder.AddActivityPropagation();
            clientBuilder.Services.AddSerializer(ConfigureEdictSerialization);
            clientBuilder.Services.AddEdict();
            clientBuilder.Services.AddSingleton(
                new NpgsqlDataSourceBuilder(ctx.PostgresConnectionString).Build());
            clientBuilder.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(serviceProvider =>
                new PostgresTableRepository<EdictDeadLetterEntry>(
                    serviceProvider.GetRequiredService<NpgsqlDataSource>(),
                    ctx.DeadLetterTableName,
                    serviceProvider.GetRequiredService<Serializer>()));
        }
    }
}

[CollectionDefinition(Name)]
public sealed class IdempotencyWindowSizePostgresCollection
    : ICollectionFixture<IdempotencyWindowSizePostgresFixture>
{
    public const string Name = "IdempotencyWindowSizePostgresCluster";
}
