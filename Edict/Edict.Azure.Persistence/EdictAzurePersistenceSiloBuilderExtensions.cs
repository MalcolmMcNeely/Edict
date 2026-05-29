using Azure.Data.Tables;
using Azure.Storage.Blobs;

using Edict.Azure.Persistence.TableStorage;
using Edict.Contracts.Configuration;
using Edict.Contracts.DeadLetter;
using Edict.Contracts.TableStorage;
using Edict.Core.Configuration;
using Edict.Core.TableStorage;

using Microsoft.Extensions.DependencyInjection;

using Orleans.Hosting;

namespace Edict.Azure.Persistence;

/// <summary>
/// Persistence-side silo-builder extension for the Azure substrate.
/// <c>AddEdictAzurePersistence</c> chains the Orleans grain-storage,
/// reminder, and table-storage primitives plus the Edict provider seams
/// (dead-letter table repository, table write-store factory) so a silo's
/// <c>Program.cs</c> reads top-to-bottom as one Action lambda instead of
/// interleaved registrations. Claim-check sits with the streams side
/// (see <c>AddEdictAzureStreams</c>) because it is driven by the queue
/// wire-cap, not grain state.
/// </summary>
public static class EdictAzurePersistenceSiloBuilderExtensions
{
    /// <summary>
    /// Registers Azure Blob grain storage for <c>edict-state</c>, Azure Table
    /// grain storage for <c>PubSubStore</c>, the Azure Table reminder service,
    /// the table write-store factory, the dead-letter table repository, and
    /// the <see cref="EdictPersistenceProviderMarker"/> the startup validator
    /// inspects.
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
            ?? throw new EdictWiringException(
                "AddEdictAzurePersistence requires either a DI-registered TableServiceClient singleton instance or one set on EdictAzurePersistenceOptions.TableServiceClient.");
        var blobClient = FindRegisteredInstance<BlobServiceClient>(silo.Services)
            ?? options.BlobServiceClient
            ?? throw new EdictWiringException(
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
