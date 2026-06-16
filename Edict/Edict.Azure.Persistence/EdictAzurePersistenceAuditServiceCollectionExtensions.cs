using Edict.Azure.Persistence.Audit;
using Edict.Contracts.Audit;
using Edict.Core.Audit;
using Edict.Core.Configuration;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Orleans.Serialization;

namespace Edict.Azure.Persistence;

/// <summary>
/// Client-side Azure audit-read extension on <see cref="IServiceCollection"/>.
/// The silo captures the audit log through
/// <see cref="EdictAzurePersistenceSiloBuilderExtensions.AddEdictAzurePersistence"/>
/// plus <c>WithAudit()</c>; a separate process that only reads it (an Orleans
/// client, a web host) registers the Azure audit stores against the same Table
/// and Blob accounts and resolves an <see cref="IEdictAuditRepository"/> over
/// them, with no grain storage, reminders, or capture path. This is the Azure
/// counterpart to <c>AddEdictPostgresAuditReader</c>; the read surface it exposes
/// is provider-agnostic, so the audit page is identical across substrates.
/// </summary>
public static class EdictAzurePersistenceAuditServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Azure-backed audit stores and the read-only
    /// <see cref="IEdictAuditRepository"/> over them. Set the same service clients
    /// and <see cref="EdictAzurePersistenceOptions.AuditTableName"/> /
    /// <see cref="EdictAzurePersistenceOptions.AuditPayloadContainerName"/> the
    /// capturing silo uses so the reader points at the same records. The clients
    /// wrap already-provisioned stores, so the registration does no I/O. Requires
    /// an Orleans <c>Serializer</c> in the container (an Orleans client host
    /// already registers one) to type a captured body back.
    /// </summary>
    public static IServiceCollection AddEdictAzureAuditReader(
        this IServiceCollection services,
        Action<EdictAzurePersistenceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new EdictAzurePersistenceOptions();
        configure(options);

        var tableClient = options.TableServiceClient
            ?? throw new EdictWiringException(
                "AddEdictAzureAuditReader requires EdictAzurePersistenceOptions.TableServiceClient.");
        var blobClient = options.BlobServiceClient
            ?? throw new EdictWiringException(
                "AddEdictAzureAuditReader requires EdictAzurePersistenceOptions.BlobServiceClient.");

        var auditTableName = options.AuditTableName;
        var auditPayloadContainerName = options.AuditPayloadContainerName;

        services.TryAddSingleton<IEdictAuditStore>(serviceProvider =>
            AzureTableAuditStore.Create(tableClient, serviceProvider.GetRequiredService<Serializer>(), auditTableName));
        services.TryAddSingleton<IEdictAuditPayloadStore>(
            _ => AzureBlobAuditPayloadStore.Create(blobClient, auditPayloadContainerName));

        return services.AddEdictAuditReader();
    }
}
