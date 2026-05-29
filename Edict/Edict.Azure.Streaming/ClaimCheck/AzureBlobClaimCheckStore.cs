using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

using Edict.Contracts.ClaimCheck;

namespace Edict.Azure.Streaming.ClaimCheck;

/// <summary>
/// Azure Blob-backed <see cref="IEdictClaimCheckStore"/> for the production
/// claim-check escape hatch. Append-only — the seam exposes no
/// delete; retention is the storage account's lifecycle policy. Key
/// generation is the store's responsibility: each payload lands at a fresh
/// GUID-based key so collisions are impossible and operator forensics can
/// click through the dead-letter row's <c>ClaimCheckKey</c> to the blob.
/// <para>
/// Lives under <c>Edict.Azure.Streaming</c> because the Azure Queue wire-cap
/// is what makes claim-check operationally required for AQS users; a Kafka
/// consumer who wants Azure-blob claim-check still has the
/// <see cref="IEdictClaimCheckStore"/> seam to wire one up directly.
/// </para>
/// </summary>
public sealed class AzureBlobClaimCheckStore : IEdictClaimCheckStore
{
    readonly BlobContainerClient _container;

    AzureBlobClaimCheckStore(BlobContainerClient container)
    {
        _container = container;
    }

    /// <summary>
    /// Constructs the store and ensures the backing container exists. The
    /// AppHost is expected to have provisioned the container against the
    /// storage account; this call is the idempotent safety net for fresh
    /// dev/test environments.
    /// </summary>
    public static async Task<AzureBlobClaimCheckStore> CreateAsync(
        BlobServiceClient blobServiceClient,
        string containerName,
        CancellationToken cancellationToken = default)
    {
        var container = blobServiceClient.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        return new AzureBlobClaimCheckStore(container);
    }

    public async Task<string> PutAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var key = $"{DateTime.UtcNow:yyyy/MM/dd}/{Guid.NewGuid():N}";
        var blob = _container.GetBlobClient(key);
        await blob.UploadAsync(BinaryData.FromBytes(payload), overwrite: false, cancellationToken: cancellationToken);
        return key;
    }

    public async Task<ReadOnlyMemory<byte>> GetAsync(string key, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(key);
        var response = await blob.DownloadContentAsync(cancellationToken);
        return response.Value.Content.ToMemory();
    }
}
