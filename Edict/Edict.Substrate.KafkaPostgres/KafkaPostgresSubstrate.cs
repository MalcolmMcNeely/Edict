using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Serialization;
using Edict.Postgres;
using Edict.Postgres.TableStorage;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Hosting;
using Orleans.Serialization;

using Testcontainers.Azurite;
using Testcontainers.PostgreSql;

namespace Edict.Substrate.KafkaPostgres;

/// <summary>
/// Brings up a Postgres + Azurite pair and hands back ConfigureSilo /
/// ConfigureClient callbacks wiring
/// <see cref="EdictAzureSiloBuilderExtensions.AddEdictAzureStreams"/> against
/// Azurite and
/// <see cref="EdictPostgresSiloBuilderExtensions.AddEdictPostgresPersistence"/>
/// against Postgres. Until the Kafka provider slice lands, this substrate runs
/// the Postgres half against AQS streams — the conformance battery still
/// proves Edict's pluggability on a second persistence backend.
/// </summary>
public sealed class KafkaPostgresSubstrate : ISubstrate
{
    public string Name => "kafkapostgres";

    public async Task<ISubstrateRuntime> StartAsync(CancellationToken ct)
    {
        var postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
        var azuriteContainer = new AzuriteBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:3.35.0")
            .WithCreateParameterModifier(p =>
            {
                p.Cmd ??= [];
                p.Cmd.Add("--skipApiVersionCheck");
            })
            .Build();
        await Task.WhenAll(postgresContainer.StartAsync(ct), azuriteContainer.StartAsync(ct));

        var postgresConnectionString = postgresContainer.GetConnectionString();
        var azuriteConnectionString = azuriteContainer.GetConnectionString();
        var tableClient = new TableServiceClient(azuriteConnectionString);
        var blobClient = new BlobServiceClient(azuriteConnectionString);
        var queueClient = new QueueServiceClient(azuriteConnectionString);

        return new KafkaPostgresSubstrateRuntime(
            postgresContainer,
            azuriteContainer,
            postgresConnectionString,
            azuriteConnectionString,
            tableClient,
            blobClient,
            queueClient);
    }
}

public sealed class KafkaPostgresSubstrateRuntime : ISubstrateRuntime
{
    readonly PostgreSqlContainer _postgresContainer;
    readonly AzuriteContainer _azuriteContainer;

    internal KafkaPostgresSubstrateRuntime(
        PostgreSqlContainer postgresContainer,
        AzuriteContainer azuriteContainer,
        string postgresConnectionString,
        string azuriteConnectionString,
        TableServiceClient tableClient,
        BlobServiceClient blobClient,
        QueueServiceClient queueClient)
    {
        _postgresContainer = postgresContainer;
        _azuriteContainer = azuriteContainer;
        PostgresConnectionString = postgresConnectionString;
        AzuriteConnectionString = azuriteConnectionString;
        TableClient = tableClient;
        BlobClient = blobClient;
        QueueClient = queueClient;

        ConfigureSilo = silo =>
        {
            silo.Services.AddSerializer(s => s
                .AddAssembly(typeof(KafkaPostgresSubstrate).Assembly)
                .AddEdictContractSerializer());
            silo.Services.AddSingleton(tableClient);
            silo.Services.AddSingleton(blobClient);
            silo.Services.AddSingleton(queueClient);
            silo.AddEdict();
            silo.AddEdictAzureStreams(o =>
            {
                o.QueueServiceClient = queueClient;
            });
            silo.AddEdictPostgresPersistence(o =>
            {
                o.ConnectionString = postgresConnectionString;
            });
        };

        ConfigureClient = client =>
        {
            client.Services.AddSerializer(s => s
                .AddAssembly(typeof(KafkaPostgresSubstrate).Assembly)
                .AddEdictContractSerializer());
            client.Services.AddEdict();
            client.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(sp =>
                new PostgresTableRepository<EdictDeadLetterEntry>(
                    postgresConnectionString,
                    "edict_dead_letter",
                    sp.GetRequiredService<Serializer>()));
        };
    }

    public string PostgresConnectionString { get; }

    public string AzuriteConnectionString { get; }

    public TableServiceClient TableClient { get; }

    public BlobServiceClient BlobClient { get; }

    public QueueServiceClient QueueClient { get; }

    public Action<ISiloBuilder> ConfigureSilo { get; }

    public Action<IClientBuilder> ConfigureClient { get; }

    public async ValueTask DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
        await _azuriteContainer.DisposeAsync();
    }
}
