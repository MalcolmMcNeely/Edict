using System.Diagnostics;
using System.Net.Sockets;

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

    public async Task<ISubstrateRuntime> StartAsync(CancellationToken ct, SubstrateStartMode mode = SubstrateStartMode.ClosedLoop)
    {
        // Azure Queue streams poll on a timer; there is no Earliest/Latest
        // analogue. Saturation mode is accepted for harness uniformity.
        _ = mode;
        var container = new AzuriteBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:3.35.0")
            .WithCreateParameterModifier(p =>
            {
                p.Cmd ??= [];
                p.Cmd.Add("--skipApiVersionCheck");
            })
            .Build();
        await container.StartAsync(ct);
        await WaitForHostEndpointsAsync(container, ct);

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

    // Testcontainers' Azurite wait strategy keys off in-container readiness,
    // not the host-side port mapping. On Podman/Windows the gvproxy forwarder
    // can lag behind the container being "ready" — the in-container Azurite
    // accepts connections, but 127.0.0.1:{mapped-port} on the host still
    // returns RST. The Azure SDK's default retry budget (~25–30 s) gives up
    // before gvproxy publishes the mapping, surfacing as a
    // "connection actively refused" AggregateException out of the first
    // CreateIfNotExistsAsync call inside AddEdictAzurePersistence.
    // This probe makes the substrate wait for host-side TCP connectivity on
    // every endpoint before handing the runtime back, so the silo configurator
    // never races the forwarder.
    static async Task WaitForHostEndpointsAsync(AzuriteContainer container, CancellationToken ct)
    {
        Uri[] endpoints =
        [
            new Uri(container.GetBlobEndpoint()),
            new Uri(container.GetQueueEndpoint()),
            new Uri(container.GetTableEndpoint()),
        ];

        var deadline = TimeSpan.FromSeconds(60);

        foreach (var endpoint in endpoints)
        {
            var stopwatch = Stopwatch.StartNew();
            SocketException? lastError = null;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var probe = new TcpClient();
                    await probe.ConnectAsync(endpoint.Host, endpoint.Port, ct);
                    lastError = null;
                    break;
                }
                catch (SocketException ex)
                {
                    lastError = ex;
                    if (stopwatch.Elapsed >= deadline)
                    {
                        break;
                    }
                    await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
                }
            }

            if (lastError is not null)
            {
                throw new InvalidOperationException(
                    $"Azurite container reported ready, but the host could not connect to {endpoint} within {deadline.TotalSeconds:F0} s. On Podman/Windows this is typically a gvproxy port-forwarder stall after rapid container churn — the in-container Azurite is reachable, the host-mapped port is not.",
                    lastError);
            }
        }
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
