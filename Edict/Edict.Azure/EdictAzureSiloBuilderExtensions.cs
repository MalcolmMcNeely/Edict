using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure.ClaimCheck;
using Edict.Azure.TableStorage;
using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core.ClaimCheck;
using Edict.Core.DeadLetter;
using Edict.Core.Outbox;
using Edict.Core.TableStorage;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;

namespace Edict.Azure;

/// <summary>
/// Provider extensions on <see cref="ISiloBuilder"/> for the Azure substrate.
/// One call per decision the consumer is making: streams provider,
/// persistence provider. Each call internally chains the Orleans
/// silo-builder primitives (<c>AddAzureQueueStreams</c>,
/// <c>AddAzureBlobGrainStorage</c>, <c>AddAzureTableGrainStorage</c>,
/// <c>UseAzureTableReminderService</c>) plus the Edict provider seams
/// (claim-check store, dead-letter table repository, table write-store
/// factory) so a silo's <c>Program.cs</c> reads top-to-bottom as three Action
/// lambdas instead of seven interleaved registrations.
/// </summary>
public static class EdictAzureSiloBuilderExtensions
{
    /// <summary>
    /// Registers the Azure Queue stream provider, the claim-check threshold
    /// (wire-cap concern, hence on the streams side), and the
    /// <see cref="EdictStreamsProviderMarker"/> the startup validator inspects.
    /// </summary>
    public static ISiloBuilder AddEdictAzureStreams(
        this ISiloBuilder silo,
        Action<EdictAzureStreamsOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new EdictAzureStreamsOptions();
        configure(options);

        silo.Services.AddSingleton<IEdictWiringMarker, EdictStreamsProviderMarker>();

        var queueClient = FindRegisteredInstance<QueueServiceClient>(silo.Services)
            ?? options.QueueServiceClient
            ?? throw new InvalidOperationException(
                "AddEdictAzureStreams requires either a DI-registered QueueServiceClient singleton instance or one set on EdictAzureStreamsOptions.QueueServiceClient.");

        silo.AddAzureQueueStreams(options.StreamProviderName, providerConfigure =>
        {
            providerConfigure.ConfigureAzureQueue(opt =>
            {
                opt.Configure(cfg => cfg.QueueServiceClient = queueClient);
                // QueueNames is the surface Orleans actually consumes; the count
                // of the list IS the fan-out. Composed with ClusterOptions so
                // the generated names match Orleans' default per-cluster scoping
                // ({providerName}-{serviceId}-{i}).
                opt.Configure<IOptions<ClusterOptions>>((cfg, clusterOpts) =>
                {
                    cfg.QueueNames = Enumerable.Range(0, options.NumQueues)
                        .Select(i => $"{options.StreamProviderName.ToLowerInvariant()}-{clusterOpts.Value.ServiceId}-{i}")
                        .ToList();
                });
            });
            providerConfigure.ConfigurePullingAgent(opt => opt.Configure(o =>
            {
                o.GetQueueMsgsTimerPeriod = options.QueuePollingPeriod;
            }));
        });

        // ClaimCheckPolicy with the configured threshold; binds against whatever
        // IEdictClaimCheckStore the persistence provider registers (or null —
        // in which case the policy never trips into the pointer branch).
        silo.Services.AddSingleton(serviceProvider => new ClaimCheckPolicy(
            serviceProvider.GetRequiredService<Serializer>(),
            options.ClaimCheckThresholdBytes,
            serviceProvider.GetService<IEdictClaimCheckStore>(),
            serviceProvider.GetRequiredService<IEventStreamAccessors>()));

        return silo;
    }

    /// <summary>
    /// Registers Azure Blob grain storage for <c>edict-state</c>, Azure Table
    /// grain storage for <c>PubSubStore</c>, the Azure Table reminder service,
    /// the table write-store factory, the dead-letter table repository, the
    /// claim-check blob store, and the <see cref="EdictPersistenceProviderMarker"/>
    /// the startup validator inspects.
    /// </summary>
    public static ISiloBuilder AddEdictAzurePersistence(
        this ISiloBuilder silo,
        Action<EdictAzurePersistenceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new EdictAzurePersistenceOptions();
        configure(options);

        silo.Services.AddSingleton<IEdictWiringMarker, EdictPersistenceProviderMarker>();

        // Resolve clients at extension-call time: walk the IServiceCollection
        // for a pre-registered singleton instance (the AddAzureClients() /
        // AddSingleton(client) power-user path); fall back to the options-bag
        // value otherwise. A factory-built registration of TableServiceClient
        // would be unresolvable here without BuildServiceProvider — that's the
        // documented limitation; the simple path (instance) wins.
        var tableClient = FindRegisteredInstance<TableServiceClient>(silo.Services)
            ?? options.TableServiceClient
            ?? throw new InvalidOperationException(
                "AddEdictAzurePersistence requires either a DI-registered TableServiceClient singleton instance or one set on EdictAzurePersistenceOptions.TableServiceClient.");
        var blobClient = FindRegisteredInstance<BlobServiceClient>(silo.Services)
            ?? options.BlobServiceClient
            ?? throw new InvalidOperationException(
                "AddEdictAzurePersistence requires either a DI-registered BlobServiceClient singleton instance or one set on EdictAzurePersistenceOptions.BlobServiceClient.");

        // PubSubStore stays on Tables — Orleans-internal, bounded shape.
        silo.AddAzureTableGrainStorage("PubSubStore", opt => opt.TableServiceClient = tableClient);

        // edict-state on Blob — single-blob ETag atomicity, no per-property cap.
        var stateContainer = options.GrainStateContainerName;
        silo.AddAzureBlobGrainStorage(stateContainer, opt =>
        {
            opt.BlobServiceClient = blobClient;
            opt.ContainerName = stateContainer;
        });

        silo.UseAzureTableReminderService(opt => opt.TableServiceClient = tableClient);

        silo.Services.AddSingleton<IEdictTableStoreFactory>(
            _ => new AzureTableWriteStoreFactory(tableClient));

        var deadLetterTable = options.DeadLetterTableName;
        silo.Services.AddSingleton<IEdictTableRepository<EdictDeadLetterEntry>>(_ =>
            new AzureTableRepository<EdictDeadLetterEntry>(tableClient, deadLetterTable));

        // Claim-check blob store. Build eagerly on the host thread — a factory-lambda
        // registration that calls .GetAwaiter().GetResult() deadlocks on the grain
        // task scheduler when the first activation resolves the store
        // (the await continuation cannot resume while the activation is blocked).
        var claimCheckContainer = options.ClaimCheckBlobContainerName;
        var claimCheckStore = AzureBlobClaimCheckStore
            .CreateAsync(blobClient, claimCheckContainer)
            .GetAwaiter().GetResult();
        silo.Services.TryAddSingleton<IEdictClaimCheckStore>(claimCheckStore);

        return silo;
    }

    static T? FindRegisteredInstance<T>(IServiceCollection services) where T : class
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];
            if (descriptor.ServiceType == typeof(T) && descriptor.ImplementationInstance is T instance)
            {
                return instance;
            }
        }
        return null;
    }
}
