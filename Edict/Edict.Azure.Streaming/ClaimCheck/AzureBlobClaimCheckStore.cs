using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

using Edict.Contracts.ClaimCheck;

namespace Edict.Azure.Streaming.ClaimCheck;

sealed class AzureBlobClaimCheckStore : IEdictClaimCheckStore
{
    readonly BlobContainerClient _container;

    AzureBlobClaimCheckStore(BlobContainerClient container)
    {
        _container = container;
    }

    // Idempotent safety net for fresh dev/test environments — the AppHost is
    // expected to have provisioned the container against the storage account.
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
