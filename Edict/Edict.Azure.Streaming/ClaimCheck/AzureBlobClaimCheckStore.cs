using Azure;
using Azure.Storage.Blobs;

using Edict.Contracts.ClaimCheck;
using Edict.Core.DeadLetter;

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
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new EdictClaimCheckFetchException(
                EdictClaimCheckFetchException.Reason.KeyMalformed,
                key,
                $"Claim-check key '{key}' is empty or whitespace.");
        }

        var blob = _container.GetBlobClient(key);
        try
        {
            var response = await blob.DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToMemory();
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            throw new EdictClaimCheckFetchException(
                EdictClaimCheckFetchException.Reason.PayloadMissing,
                key,
                $"Claim-check payload not found for key '{key}'.");
        }
    }
}
