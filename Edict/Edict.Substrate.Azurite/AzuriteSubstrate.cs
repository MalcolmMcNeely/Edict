using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure;
using Edict.Azure.TableStorage;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core;
using Edict.Core.Serialization;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Hosting;
using Orleans.Serialization;

using Testcontainers.Azurite;

namespace Edict.Substrate.Azurite;

/// <summary>
/// Brings up an Azurite container and hands back ConfigureSilo/ConfigureClient
/// callbacks wiring <see cref="EdictAzureSiloBuilderExtensions.AddEdictAzureStreams"/>
/// and <see cref="EdictAzureSiloBuilderExtensions.AddEdictAzurePersistence"/> at
/// the container endpoints. Workload-specific repositories (a harness's own
/// projection row types) stay in the harness — this substrate only registers
/// framework-level surfaces (ADR-0030).
/// </summary>
public sealed class AzuriteSubstrate : ISubstrate
{
    public const string GrainStateContainerName = "edict-state";
    public const string ClaimCheckBlobContainerName = "edict-claim-check";
    public const string DeadLetterTableName = "edictdeadletter";

    public string Name => "azure";

    public async Task<ISubstrateRuntime> StartAsync(CancellationToken ct)
    {
        var container = new AzuriteBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:3.35.0")
            .WithCreateParameterModifier(p =>
            {
                p.Cmd ??= [];
                p.Cmd.Add("--skipApiVersionCheck");
            })
            .Build();
        await container.StartAsync(ct);

        var connectionString = container.GetConnectionString();
        var tableClient = new TableServiceClient(connectionString);
        var blobClient = new BlobServiceClient(connectionString);
        var queueClient = new QueueServiceClient(connectionString);

        return new AzuriteSubstrateRuntime(
            container,
            connectionString,
            tableClient,
            blobClient,
            queueClient);
    }
}

public sealed class AzuriteSubstrateRuntime : ISubstrateRuntime
{
    readonly AzuriteContainer _container;

    internal AzuriteSubstrateRuntime(
        AzuriteContainer container,
        string connectionString,
        TableServiceClient tableClient,
        BlobServiceClient blobClient,
        QueueServiceClient queueClient)
    {
        _container = container;
        ConnectionString = connectionString;
        TableClient = tableClient;
        BlobClient = blobClient;
        QueueClient = queueClient;

        ConfigureSilo = silo =>
        {
            silo.Services.AddSerializer(s => s
                .AddAssembly(typeof(AzuriteSubstrate).Assembly)
                .AddEdictContractSerializer());
            silo.Services.AddSingleton(tableClient);
            silo.Services.AddSingleton(blobClient);
            silo.Services.AddSingleton(queueClient);
            silo.AddEdict();
            silo.AddEdictAzureStreams(o =>
            {
                o.QueueServiceClient = queueClient;
            });
            silo.AddEdictAzurePersistence(o =>
            {
                o.GrainStateContainerName = AzuriteSubstrate.GrainStateContainerName;
                o.ClaimCheckBlobContainerName = AzuriteSubstrate.ClaimCheckBlobContainerName;
                o.DeadLetterTableName = AzuriteSubstrate.DeadLetterTableName;
                o.TableServiceClient = tableClient;
                o.BlobServiceClient = blobClient;
            });
        };

        ConfigureClient = client =>
        {
            client.Services.AddSerializer(s => s
                .AddAssembly(typeof(AzuriteSubstrate).Assembly)
                .AddEdictContractSerializer());
            client.Services.AddSingleton(tableClient);
            client.Services.AddEdict();
            // Framework-level dead-letter repo lives in the substrate — every
            // Edict consumer needs to read the dead-letter table. Workload-
            // specific row repositories belong in the harness, not here.
            client.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(
                _ => new AzureTableRepository<EdictDeadLetterEntry>(
                    tableClient, AzuriteSubstrate.DeadLetterTableName));
        };
    }

    public string ConnectionString { get; }

    public TableServiceClient TableClient { get; }

    public BlobServiceClient BlobClient { get; }

    public QueueServiceClient QueueClient { get; }

    public Action<ISiloBuilder> ConfigureSilo { get; }

    public Action<IClientBuilder> ConfigureClient { get; }

    public IEdictTableRepository<TRow> CreateRowRepository<TRow>(IServiceProvider sp, string tableName)
        where TRow : class, new()
    {
        ArgumentNullException.ThrowIfNull(sp);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        return new AzureTableRepository<TRow>(sp.GetRequiredService<TableServiceClient>(), tableName);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
