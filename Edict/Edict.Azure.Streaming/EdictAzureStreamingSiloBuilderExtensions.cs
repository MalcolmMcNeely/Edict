using Azure.Storage.Blobs;
using Azure.Storage.Queues;

using Edict.Azure.Streaming.ClaimCheck;
using Edict.Contracts.ClaimCheck;
using Edict.Contracts.Configuration;
using Edict.Core.ClaimCheck;
using Edict.Core.Configuration;
using Edict.Core.Outbox;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;

namespace Edict.Azure.Streaming;

/// <summary>
/// Streaming-side silo-builder extensions for the Azure substrate.
/// <c>AddEdictAzureStreams</c> wires the Orleans AQS stream provider and
/// the <see cref="ClaimCheckPolicy"/>; <c>AddEdictAzureBlobClaimCheck</c>
/// wires the Azure-blob-backed claim-check store. The two are split so an
/// AQS consumer pairing with Postgres persistence can take the streams
/// extension without dragging the Azure-blob store into a wiring race with
/// <c>AddEdictPostgresPersistence</c>'s Postgres-backed store.
/// </summary>
public static class EdictAzureStreamingSiloBuilderExtensions
{
    /// <summary>
    /// Registers the Azure Queue stream provider, the claim-check threshold
    /// (wire-cap concern, hence on the streams side), the
    /// <see cref="ClaimCheckPolicy"/> resolving whatever
    /// <see cref="IEdictClaimCheckStore"/> is in DI, and the
    /// <see cref="EdictStreamsProviderMarker"/> the startup validator
    /// inspects. The claim-check store itself is registered separately —
    /// either via <see cref="AddEdictAzureBlobClaimCheck(ISiloBuilder, Action{EdictAzureBlobClaimCheckOptions})"/>
    /// for Azure-blob substrate, or by the persistence extension a Postgres
    /// consumer wires up.
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
            ?? throw new EdictWiringException(
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
        // IEdictClaimCheckStore the persistence/claim-check provider registers
        // (or null — in which case the policy never trips into the pointer
        // branch).
        silo.Services.AddSingleton(serviceProvider => new ClaimCheckPolicy(
            serviceProvider.GetRequiredService<Serializer>(),
            options.ClaimCheckThresholdBytes,
            serviceProvider.GetService<IEdictClaimCheckStore>(),
            serviceProvider.GetRequiredService<IEventStreamAccessors>()));

        return silo;
    }

    /// <summary>
    /// Registers the Azure-blob-backed
    /// <see cref="IEdictClaimCheckStore"/>. Idempotent via
    /// <see cref="ServiceCollectionDescriptorExtensions.TryAddSingleton{TService}(IServiceCollection, TService)"/>
    /// — a previously-registered store wins, so wire order is forgiving.
    /// Call this when an AQS consumer wants the bytes physically in Azure
    /// Blob (the common case alongside <c>AddEdictAzurePersistence</c>).
    /// Skip it when an AQS consumer wants the bytes in Postgres
    /// (<c>AddEdictPostgresPersistence</c> registers its own store).
    /// </summary>
    public static ISiloBuilder AddEdictAzureBlobClaimCheck(
        this ISiloBuilder silo,
        Action<EdictAzureBlobClaimCheckOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new EdictAzureBlobClaimCheckOptions();
        configure(options);

        var blobClient = FindRegisteredInstance<BlobServiceClient>(silo.Services)
            ?? options.BlobServiceClient
            ?? throw new EdictWiringException(
                "AddEdictAzureBlobClaimCheck requires either a DI-registered BlobServiceClient singleton instance or one set on EdictAzureBlobClaimCheckOptions.BlobServiceClient.");

        // Build eagerly on the host thread — a factory-lambda registration
        // that calls .GetAwaiter().GetResult() deadlocks on the grain task
        // scheduler when the first activation resolves the store (the await
        // continuation cannot resume while the activation is blocked).
        var store = AzureBlobClaimCheckStore
            .CreateAsync(blobClient, options.ContainerName)
            .GetAwaiter().GetResult();
        silo.Services.TryAddSingleton<IEdictClaimCheckStore>(store);

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
