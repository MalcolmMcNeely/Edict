using System.Net.Sockets;

using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using DotNet.Testcontainers.Builders;

using Edict.Azure.Persistence;
using Edict.Azure.Persistence.TableStorage;
using Edict.Azure.Streaming;
using Edict.Azure.Streaming.ClaimCheck;
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
/// callbacks wiring <see cref="EdictAzureStreamingSiloBuilderExtensions.AddEdictAzureStreams"/>
/// and <see cref="EdictAzurePersistenceSiloBuilderExtensions.AddEdictAzurePersistence"/> at
/// the container endpoints. Workload-specific repositories (a harness's own
/// projection row types) stay in the harness — this substrate only registers
/// framework-level surfaces.
/// </summary>
public sealed class AzuriteSubstrate : ISubstrate
{
    public const string GrainStateContainerName = "edict-state";
    public const string ClaimCheckBlobContainerName = "edict-claim-check";

    readonly TimeProvider _timeProvider;
    readonly BringUpTuning _tuning;
    readonly SubstrateBringUpPolicy _bringUpPolicy;

    public AzuriteSubstrate()
        : this(TimeProvider.System, BringUpTuning.FromEnvironment())
    {
    }

    public AzuriteSubstrate(TimeProvider timeProvider, BringUpTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(tuning);
        _timeProvider = timeProvider;
        _tuning = tuning;
        _bringUpPolicy = new SubstrateBringUpPolicy(timeProvider);
    }

    public string Name => "azure";

    public Task<ISubstrateRuntime> StartAsync(CancellationToken cancellationToken, SubstrateStartMode mode = SubstrateStartMode.ClosedLoop)
    {
        // Azure Queue streams poll on a timer; there is no Earliest/Latest
        // analogue. Saturation mode is accepted for harness uniformity.
        _ = mode;
        return _bringUpPolicy.BringUpAsync(
            Name,
            [StartContainerAsync],
            disposables => Build((AzuriteContainer)disposables[0]),
            _tuning,
            cancellationToken);
    }

    async Task<IAsyncDisposable> StartContainerAsync(BringUpTuning tuning, CancellationToken cancellationToken)
    {
        var container = new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:3.35.0")
            .WithCreateParameterModifier(p =>
            {
                p.Cmd ??= [];
                p.Cmd.Add("--skipApiVersionCheck");
            })
            // The stock module wait keys off an in-container log line under the
            // ~1 h default timeout; bound it instead to the lowered tuning value
            // against the container's own blob/queue/table ports, so an
            // in-container readiness hang fails fast into a fresh-container retry.
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilInternalTcpPortIsAvailable(10000, waitStrategy => waitStrategy.WithTimeout(tuning.TestcontainersWaitTimeout))
                .UntilInternalTcpPortIsAvailable(10001, waitStrategy => waitStrategy.WithTimeout(tuning.TestcontainersWaitTimeout))
                .UntilInternalTcpPortIsAvailable(10002, waitStrategy => waitStrategy.WithTimeout(tuning.TestcontainersWaitTimeout)))
            .Build();
        try
        {
            await container.StartAsync(cancellationToken);
            await WaitForHostEndpointsAsync(container, tuning, cancellationToken);
            return container;
        }
        catch
        {
            // Release the container before the retry: a stalled host-port
            // forwarder never clears on the same mapping, so the next attempt
            // must start from a freshly created container.
            try
            {
                await container.DisposeAsync();
            }
            catch
            {
                // A teardown failure must not mask the bring-up failure that triggered it.
            }
            throw;
        }
    }

    static AzuriteSubstrateRuntime Build(AzuriteContainer container)
    {
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
    async Task WaitForHostEndpointsAsync(AzuriteContainer container, BringUpTuning tuning, CancellationToken cancellationToken)
    {
        Uri[] endpoints =
        [
            new Uri(container.GetBlobEndpoint()),
            new Uri(container.GetQueueEndpoint()),
            new Uri(container.GetTableEndpoint()),
        ];

        foreach (var endpoint in endpoints)
        {
            var startTimestamp = _timeProvider.GetTimestamp();
            SocketException? lastError = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var probe = new TcpClient();
                    await probe.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken);
                    lastError = null;
                    break;
                }
                catch (SocketException exception)
                {
                    lastError = exception;
                    if (_timeProvider.GetElapsedTime(startTimestamp) >= tuning.HostReadinessProbeDeadline)
                    {
                        break;
                    }
                    await Task.Delay(tuning.HostReadinessProbePollInterval, _timeProvider, cancellationToken);
                }
            }

            if (lastError is not null)
            {
                throw new InvalidOperationException(
                    $"Azurite container reported ready, but the host could not connect to {endpoint} within {tuning.HostReadinessProbeDeadline.TotalSeconds:F0} s. On Podman/Windows this is typically a gvproxy port-forwarder stall after rapid container churn — the in-container Azurite is reachable, the host-mapped port is not.",
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
            silo.AddEdictAzureBlobClaimCheck(o =>
            {
                o.ContainerName = AzuriteSubstrate.ClaimCheckBlobContainerName;
                o.BlobServiceClient = blobClient;
            });
            silo.AddEdictAzurePersistence(o =>
            {
                o.GrainStateContainerName = AzuriteSubstrate.GrainStateContainerName;
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
            // The dead-letter forensic facade reads through the projection grain
            // now, so AddEdict()'s auto-registered IEdictListProjectionReader serves it
            // with no substrate-side repository wiring.
        };
    }

    public string ConnectionString { get; }

    public TableServiceClient TableClient { get; }

    public BlobServiceClient BlobClient { get; }

    public QueueServiceClient QueueClient { get; }

    public Action<ISiloBuilder> ConfigureSilo { get; }

    public Action<IClientBuilder> ConfigureClient { get; }

    public IEdictTableWriteStore<TRow> CreateRowStore<TRow>(IServiceProvider serviceProvider, string tableName)
        where TRow : class, new()
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        var tableClient = serviceProvider.GetRequiredService<TableServiceClient>().GetTableClient(tableName);
        return new AzureTableWriteStore<TRow>(tableClient);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
